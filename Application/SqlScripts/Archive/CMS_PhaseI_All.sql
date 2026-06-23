
/*
    Phase I - SQL Database Delivery - Full Combined Deployment
    Updated to include dbo.tUserPermissions.

    -------------------------------------------------------------------------
    DEPLOYMENT MODE SWITCH  (#DeploySettings.FullReset)
    -------------------------------------------------------------------------
    This script is controlled by a single flag set in the preamble batch
    below. Update the INSERT INTO #DeploySettings line before running.

      FullReset = 1   (DEFAULT)
          Drops and recreates the BMS_CMS database from scratch. Use for
          greenfield deploys, dev/QA refreshes, and any time you need to
          guarantee the live schema matches this file exactly.
          *** ALL DATA IN BMS_CMS IS LOST. ***

      FullReset = 0
          Leaves the existing BMS_CMS database in place (creates it only if
          missing). Every IF OBJECT_ID(...) IS NULL guard in this file will
          then create only the tables that do not yet exist; any existing
          table keeps its current column shape. CREATE OR ALTER blocks for
          views and stored procedures still run, so view/SP definitions are
          always brought up to date. Use this mode only when you already
          have data in BMS_CMS that must survive the deployment.

    NOTE: when FullReset = 0 and the legacy table shapes differ from this
    file (e.g. an older deploy created dbo.tUser without UserType/StatusID/
    IsDeleted/etc.), the application will throw "Invalid column name" at
    runtime. In that case either flip FullReset back to 1 or hand-patch the
    legacy tables with targeted ALTER TABLE statements.
    -------------------------------------------------------------------------
*/

SET NOCOUNT ON;
GO

IF OBJECT_ID('tempdb..#DeploySettings') IS NOT NULL
    DROP TABLE #DeploySettings;
CREATE TABLE #DeploySettings (FullReset BIT NOT NULL);
INSERT INTO #DeploySettings (FullReset) VALUES (1);   -- <<< edit me (1 = greenfield wipe, 0 = preserve)
GO


/* ======================== 01_CreateDatabase.sql ======================== */
/*
    Phase I - SQL Database Delivery
    Script: 01_CreateDatabase.sql
    Purpose: Create or rebuild the BMS_CMS database according to the
             #DeploySettings.FullReset flag set in the preamble.
    Target: SQL Server
*/

USE master;
GO

IF (SELECT FullReset FROM #DeploySettings) = 1
BEGIN
    PRINT N'#DeploySettings.FullReset = 1 — dropping and recreating BMS_CMS.';
    IF DB_ID(N'BMS_CMS') IS NOT NULL
    BEGIN
        EXEC ('ALTER DATABASE BMS_CMS SET SINGLE_USER WITH ROLLBACK IMMEDIATE;');
        EXEC ('DROP DATABASE BMS_CMS;');
    END;
    EXEC ('CREATE DATABASE BMS_CMS;');
END
ELSE
BEGIN
    PRINT N'#DeploySettings.FullReset = 0 — preserving existing BMS_CMS (creating it only if missing).';
    IF DB_ID(N'BMS_CMS') IS NULL
        EXEC ('CREATE DATABASE BMS_CMS;');
END;
GO

USE BMS_CMS;
GO

/*
CMS Phase I Database Build
Generated from CMS ERD.xlsx tabs:
- Handoff
- ERD v2
- Stored Procedures
- Seed Data
- Delete Policy
- T-SQL Templates
- Required SPs
- Test Scenarios
- Sheet1

Design notes:
1. Phase I follows ERD v2: Client is the business entity; Project is the operational entity.
2. BusinessUnit is derived from tBusinessClass for clients.
3. Soft delete applies to tClient, tProject, tUser, tUserGroup, and tService.
4. Lookup tables are retired by IsActive = 0, not deleted.
*/

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/* ============================================================
   01. Lookup and core tables
   ============================================================ */
--Business Unit  select * from tbusinessunit
CREATE OR ALTER PROCEDURE dbo.usp_Create_tBusinessUnit
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID(N'dbo.tBusinessUnit', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.tBusinessUnit
        (
            BusinessUnitID INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tBusinessUnit PRIMARY KEY,
            BusinessUnitName VARCHAR(100) NOT NULL,
            BusinessUnitDescription VARCHAR(255) NULL,
            CreatedDate DATETIME2(0) NOT NULL CONSTRAINT DF_tBusinessUnit_CreatedDate DEFAULT (SYSUTCDATETIME()),
            ModifiedDate DATETIME2(0) NULL,
            IsActive BIT NOT NULL CONSTRAINT DF_tBusinessUnit_IsActive DEFAULT (1),
                CONSTRAINT UQ_tBusinessUnit_BusinessUnitName UNIQUE (BusinessUnitName)
        );
    END;
END;
GO

--Business Class  select * from tbusinessclass
CREATE OR ALTER PROCEDURE dbo.usp_Create_tBusinessClass
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID(N'dbo.tBusinessClass', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.tBusinessClass
        (
            BusinessClassID INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tBusinessClass PRIMARY KEY,
            BusinessClassName VARCHAR(100) NOT NULL,
            BusinessClassDescription VARCHAR(255) NULL,
            BusinessUnitID INT NOT NULL,
            CreatedDate DATETIME2(0) NOT NULL CONSTRAINT DF_tBusinessClass_CreatedDate DEFAULT (SYSUTCDATETIME()),
            ModifiedDate DATETIME2(0) NULL,
            IsActive BIT NOT NULL CONSTRAINT DF_tBusinessClass_IsActive DEFAULT (1),
                CONSTRAINT UQ_tBusinessClass_BusinessUnitID_BusinessClassName UNIQUE (BusinessUnitID, BusinessClassName),
                CONSTRAINT FK_tBusinessClass_tBusinessUnit FOREIGN KEY (BusinessUnitID)
                    REFERENCES dbo.tBusinessUnit(BusinessUnitID) ON DELETE NO ACTION
        );
        CREATE INDEX IX_tBusinessClass_BusinessUnitID ON dbo.tBusinessClass(BusinessUnitID);
    END;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Create_tBusinessClassTag
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID(N'dbo.tBusinessClassTag', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.tBusinessClassTag
        (
            BusinessClassID INT NOT NULL,
            TagID INT NOT NULL,
            IsActive BIT NOT NULL CONSTRAINT DF_tBusinessClassTag_IsActive DEFAULT (1),
            CONSTRAINT PK_tBusinessClassTag PRIMARY KEY (BusinessClassID, TagID),
            CONSTRAINT FK_tBusinessClassTag_tBusinessClass FOREIGN KEY (BusinessClassID)
                REFERENCES dbo.tBusinessClass(BusinessClassID) ON DELETE CASCADE,
				CONSTRAINT FK_tBusinessClassTag_tTag FOREIGN KEY (TagID)
					REFERENCES dbo.tTag(TagID) ON DELETE NO ACTION
        );
        CREATE INDEX IX_tBusinessClassTag_TagID ON dbo.tBusinessClassTag(TagID);
    END;
END;
GO

--Client
CREATE OR ALTER PROCEDURE dbo.usp_Create_tClient
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID(N'dbo.tClient', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.tClient
        (
            ClientID INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tClient PRIMARY KEY,
            ClientName VARCHAR(150) NOT NULL,
            ClientShortName VARCHAR(50) NULL,
            ClientTypeID INT NOT NULL,
            MGA VARCHAR(100) NULL,
            ProgramID INT NULL,
            BusinessClassID INT NOT NULL,
            StatusID INT NOT NULL,
            CreatedDate DATETIME2(0) NOT NULL CONSTRAINT DF_tClient_CreatedDate DEFAULT (SYSUTCDATETIME()),
            ModifiedDate DATETIME2(0) NULL,
            IsDeleted BIT NOT NULL CONSTRAINT DF_tClient_IsDeleted DEFAULT (0),
            DeletedDate DATETIME2(0) NULL,
            IsActive BIT NOT NULL CONSTRAINT DF_tClient_IsActive DEFAULT (1),
            CONSTRAINT FK_tClient_tBusinessClass FOREIGN KEY (BusinessClassID)
                REFERENCES dbo.tBusinessClass(BusinessClassID) ON DELETE NO ACTION,
            CONSTRAINT FK_tClient_tClientType FOREIGN KEY (ClientTypeID)
                REFERENCES dbo.tClientType(ClientTypeID) ON DELETE NO ACTION,
            CONSTRAINT FK_tClient_tStatus FOREIGN KEY (StatusID)
                REFERENCES dbo.tStatus(StatusID) ON DELETE NO ACTION
        );
        CREATE UNIQUE INDEX UQ_tClient_ClientName_Active ON dbo.tClient(ClientName) WHERE IsDeleted = 0;
        CREATE INDEX IX_tClient_BusinessClassID ON dbo.tClient(BusinessClassID);
        CREATE INDEX IX_tClient_StatusID ON dbo.tClient(StatusID);
    END;
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[usp_Create_tClientType]
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID(N'dbo.tClientType', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.tClientType
        (
            ClientTypeID INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tClientType PRIMARY KEY,
            CTName VARCHAR(100) NOT NULL,
            CTDescription VARCHAR(255) NULL,
            StatusID INT,
            CreatedDate DATETIME2(0) NOT NULL CONSTRAINT DF_tClientType_CreatedDate DEFAULT (SYSUTCDATETIME()),
            ModifiedDate DATETIME2(0) NULL,
            IsActive BIT NOT NULL CONSTRAINT DF_tClientType_IsActive DEFAULT (1),
            CONSTRAINT UQ_tClientType_CTName UNIQUE (CTName)
        );
    END;
END;

GO

CREATE OR ALTER PROCEDURE dbo.usp_Create_tStatusType
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID(N'dbo.tStatusType', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.tStatusType
        (
            StatusTypeID INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tStatusType PRIMARY KEY,
            TypeName VARCHAR(30) NOT NULL,
            IsActive BIT NOT NULL CONSTRAINT DF_tStatusType_IsActive DEFAULT (1),
            CONSTRAINT UQ_tStatusType_TypeName UNIQUE (TypeName)
        );
    END;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Create_tStatus
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID(N'dbo.tStatus', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.tStatus
        (
            StatusID INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tStatus PRIMARY KEY,
            StatusTypeID INT NOT NULL,
            StatusCode VARCHAR(20) NOT NULL,
            StatusDescription VARCHAR(255) NULL,
            IsActive BIT NOT NULL CONSTRAINT DF_tStatus_IsActive DEFAULT (1),
            CONSTRAINT UQ_tStatus_StatusTypeID_StatusCode UNIQUE (StatusTypeID, StatusCode),
            CONSTRAINT FK_tStatus_tStatusType FOREIGN KEY (StatusTypeID)
                REFERENCES dbo.tStatusType(StatusTypeID) ON DELETE NO ACTION
        );
        CREATE INDEX IX_tStatus_StatusTypeID ON dbo.tStatus(StatusTypeID);
    END;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Create_tStatusAppliesTo
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID(N'dbo.tStatusAppliesTo', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.tStatusAppliesTo
        (
            StatusID INT NOT NULL,
            EntityType VARCHAR(30) NOT NULL,
            Notes VARCHAR(255) NULL,
            CONSTRAINT PK_tStatusAppliesTo PRIMARY KEY (StatusID, EntityType),
            CONSTRAINT FK_tStatusAppliesTo_tStatus FOREIGN KEY (StatusID)
                REFERENCES dbo.tStatus(StatusID) ON DELETE CASCADE
        );
        CREATE INDEX IX_tStatusAppliesTo_EntityType ON dbo.tStatusAppliesTo(EntityType);
    END;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Create_tProjectType
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID(N'dbo.tProjectType', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.tProjectType
        (
            ProjectTypeID INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tProjectType PRIMARY KEY,
            PTCode VARCHAR(20) NOT NULL,
            PTDescription VARCHAR(255) NULL,
            CreatedDate DATETIME2(0) NOT NULL CONSTRAINT DF_tProjectType_CreatedDate DEFAULT (SYSUTCDATETIME()),
            ModifiedDate DATETIME2(0) NULL,
            IsActive BIT NOT NULL CONSTRAINT DF_tProjectType_IsActive DEFAULT (1),
            CONSTRAINT UQ_tProjectType_PTCode UNIQUE (PTCode)
        );
    END;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Create_tServiceCategory
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID(N'dbo.tServiceCategory', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.tServiceCategory
        (
            ServiceCategoryID INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tServiceCategory PRIMARY KEY,
            CategoryName VARCHAR(100) NOT NULL,
            CatFunction VARCHAR(100) NULL,
            CreatedDate DATETIME2(0) NOT NULL CONSTRAINT DF_tServiceCategory_CreatedDate DEFAULT (SYSUTCDATETIME()),
            ModifiedDate DATETIME2(0) NULL,
            IsActive BIT NOT NULL CONSTRAINT DF_tServiceCategory_IsActive DEFAULT (1),
            CONSTRAINT UQ_tServiceCategory_CategoryName UNIQUE (CategoryName)
        );
    END;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Create_tUserRole
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID(N'dbo.tUserRole', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.tUserRole
        (
            UserRoleID INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tUserRole PRIMARY KEY,
            UserRoleName VARCHAR(100) NOT NULL,
            RoleDescription VARCHAR(255) NULL,
            Tag1 VARCHAR(50) NULL,
            Tag2 VARCHAR(50) NULL,
            Tag3 VARCHAR(50) NULL,
            StatusID INT NULL,
            CreatedDate DATETIME2(0) NOT NULL CONSTRAINT DF_tUserRole_CreatedDate DEFAULT (SYSUTCDATETIME()),
            ModifiedDate DATETIME2(0) NULL,
            IsActive BIT NOT NULL CONSTRAINT DF_tUserRole_IsActive DEFAULT (1),
            CONSTRAINT UQ_tUserRole_UserRoleName UNIQUE (UserRoleName),
            CONSTRAINT FK_tUserRole_tStatus FOREIGN KEY (StatusID)
                REFERENCES dbo.tStatus(StatusID) ON DELETE NO ACTION
        );
        CREATE INDEX IX_tUserRole_StatusID ON dbo.tUserRole(StatusID);
    END;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Create_tTag
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID(N'dbo.tTag', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.tTag
        (
            TagID INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tTag PRIMARY KEY,
            TagName VARCHAR(50) NOT NULL,
            TagScope VARCHAR(30) NOT NULL,
            CreatedDate DATETIME2(0) NOT NULL CONSTRAINT DF_tTag_CreatedDate DEFAULT (SYSUTCDATETIME()),
            IsActive BIT NOT NULL CONSTRAINT DF_tTag_IsActive DEFAULT (1),
            CONSTRAINT UQ_tTag_TagName_TagScope UNIQUE (TagName, TagScope)
        );
    END;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Create_tUser
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID(N'dbo.tUser', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.tUser
        (
            UserID INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tUser PRIMARY KEY,
            FirstName VARCHAR(50) NOT NULL,
            LastName VARCHAR(50) NOT NULL,
            Title VARCHAR(100) NULL,
            Email VARCHAR(150) NOT NULL,
            Phone VARCHAR(30) NULL,
            Company VARCHAR(150) NULL,
            Department VARCHAR(100) NULL,
            UserType VARCHAR(20) NOT NULL,
            StatusID INT NOT NULL,
            CreatedDate DATETIME2(0) NOT NULL CONSTRAINT DF_tUser_CreatedDate DEFAULT (SYSUTCDATETIME()),
            ModifiedDate DATETIME2(0) NULL,
            IsDeleted BIT NOT NULL CONSTRAINT DF_tUser_IsDeleted DEFAULT (0),
            DeletedDate DATETIME2(0) NULL,
            IsActive BIT NOT NULL CONSTRAINT DF_tUser_IsActive DEFAULT (1),
            CONSTRAINT CK_tUser_UserType CHECK (UserType IN ('Internal','External')),
            CONSTRAINT FK_tUser_tStatus FOREIGN KEY (StatusID)
                REFERENCES dbo.tStatus(StatusID) ON DELETE NO ACTION
        );
        CREATE UNIQUE INDEX UQ_tUser_Email_Active ON dbo.tUser(Email) WHERE IsDeleted = 0;
        CREATE INDEX IX_tUser_StatusID ON dbo.tUser(StatusID);
    END;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Create_tUserAddress
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID(N'dbo.tUserAddress', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.tUserAddress
        (
            AddressID INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tUserAddress PRIMARY KEY,
            UserID INT NOT NULL,
            AddressType VARCHAR(20) NOT NULL,
            Address1 VARCHAR(200) NOT NULL,
            Address2 VARCHAR(200) NULL,
            City VARCHAR(100) NOT NULL,
            State VARCHAR(50) NULL,
            PostalCode VARCHAR(20) NULL,
            Country VARCHAR(50) NOT NULL,
            CreatedDate DATETIME2(0) NOT NULL CONSTRAINT DF_tUserAddress_CreatedDate DEFAULT (SYSUTCDATETIME()),
            ModifiedDate DATETIME2(0) NULL,
            IsActive BIT NOT NULL CONSTRAINT DF_tUserAddress_IsActive DEFAULT (1),
            CONSTRAINT FK_tUserAddress_tUser FOREIGN KEY (UserID)
                REFERENCES dbo.tUser(UserID) ON DELETE CASCADE
        );
        CREATE INDEX IX_tUserAddress_UserID ON dbo.tUserAddress(UserID);
    END;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Create_tUserGroup
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID(N'dbo.tUserGroup', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.tUserGroup
        (
            UGID INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tUserGroup PRIMARY KEY,
            UserGroupNumber VARCHAR(50) NOT NULL,
            UGName VARCHAR(100) NOT NULL,
            UserRoleID INT NULL,
            StatusID INT NOT NULL,
            CreatedDate DATETIME2(0) NOT NULL CONSTRAINT DF_tUserGroup_CreatedDate DEFAULT (SYSUTCDATETIME()),
            ModifiedDate DATETIME2(0) NULL,
            IsDeleted BIT NOT NULL CONSTRAINT DF_tUserGroup_IsDeleted DEFAULT (0),
            DeletedDate DATETIME2(0) NULL,
					CONSTRAINT FK_tUserGroup_tUserRole FOREIGN KEY (UserRoleID)
						REFERENCES dbo.tUserRole(UserRoleID) ON DELETE NO ACTION,
					CONSTRAINT FK_tUserGroup_tStatus FOREIGN KEY (StatusID)
						REFERENCES dbo.tStatus(StatusID) ON DELETE NO ACTION
        );
        CREATE UNIQUE INDEX UQ_tUserGroup_UserGroupNumber_Active ON dbo.tUserGroup(UserGroupNumber) WHERE IsDeleted = 0;
        CREATE INDEX IX_tUserGroup_UserRoleID ON dbo.tUserGroup(UserRoleID);
        CREATE INDEX IX_tUserGroup_StatusID ON dbo.tUserGroup(StatusID);
    END;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Create_tUserGroupUser
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID(N'dbo.tUserGroupUser', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.tUserGroupUser
        (
            UGID INT NOT NULL,
            UserID INT NOT NULL,
            UserRoleID INT NULL,
            JoinedDate DATETIME2(0) NOT NULL CONSTRAINT DF_tUserGroupUser_JoinedDate DEFAULT (SYSUTCDATETIME()),
            StatusID INT NOT NULL,
            IsActive BIT NOT NULL CONSTRAINT DF_tUserGroupUser_IsActive DEFAULT (1),
            CONSTRAINT PK_tUserGroupUser PRIMARY KEY (UGID, UserID),
            CONSTRAINT FK_tUserGroupUser_tUserGroup FOREIGN KEY (UGID)
                REFERENCES dbo.tUserGroup(UGID) ON DELETE CASCADE,
            CONSTRAINT FK_tUserGroupUser_tUser FOREIGN KEY (UserID)
                REFERENCES dbo.tUser(UserID) ON DELETE CASCADE,
            CONSTRAINT FK_tUserGroupUser_tUserRole FOREIGN KEY (UserRoleID)
                REFERENCES dbo.tUserRole(UserRoleID) ON DELETE NO ACTION,
            CONSTRAINT FK_tUserGroupUser_tStatus FOREIGN KEY (StatusID)
                REFERENCES dbo.tStatus(StatusID) ON DELETE NO ACTION
        );
        CREATE INDEX IX_tUserGroupUser_UserID ON dbo.tUserGroupUser(UserID);
        CREATE INDEX IX_tUserGroupUser_UserRoleID ON dbo.tUserGroupUser(UserRoleID);
        CREATE INDEX IX_tUserGroupUser_StatusID ON dbo.tUserGroupUser(StatusID);
    END;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Create_tProject
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID(N'dbo.tProject', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.tProject
        (
            ProjectID INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tProject PRIMARY KEY,
            ProjectNumber VARCHAR(50) NOT NULL,
            ProjectName VARCHAR(200) NOT NULL,
            ClientID INT NOT NULL,
            UGID INT NOT NULL,
            ProjectTypeID INT NOT NULL,
            RenewalDate DATE NULL,
            DueDate DATE NULL,
            StatusID INT NOT NULL,
            CreatedDate DATETIME2(0) NOT NULL CONSTRAINT DF_tProject_CreatedDate DEFAULT (SYSUTCDATETIME()),
            ModifiedDate DATETIME2(0) NULL,
            IsDeleted BIT NOT NULL CONSTRAINT DF_tProject_IsDeleted DEFAULT (0),
            DeletedDate DATETIME2(0) NULL,
            CONSTRAINT FK_tProject_tClient FOREIGN KEY (ClientID)
                REFERENCES dbo.tClient(ClientID) ON DELETE NO ACTION,
            CONSTRAINT FK_tProject_tUserGroup FOREIGN KEY (UGID)
                REFERENCES dbo.tUserGroup(UGID) ON DELETE NO ACTION,
            CONSTRAINT FK_tProject_tProjectType FOREIGN KEY (ProjectTypeID)
                REFERENCES dbo.tProjectType(ProjectTypeID) ON DELETE NO ACTION,
            CONSTRAINT FK_tProject_tStatus FOREIGN KEY (StatusID)
                REFERENCES dbo.tStatus(StatusID) ON DELETE NO ACTION
        );
        CREATE UNIQUE INDEX UQ_tProject_ProjectNumber_Active ON dbo.tProject(ProjectNumber) WHERE IsDeleted = 0;
        CREATE INDEX IX_tProject_ClientID ON dbo.tProject(ClientID);
        CREATE INDEX IX_tProject_UGID ON dbo.tProject(UGID);
        CREATE INDEX IX_tProject_ProjectTypeID ON dbo.tProject(ProjectTypeID);
        CREATE INDEX IX_tProject_StatusID ON dbo.tProject(StatusID);
        CREATE INDEX IX_tProject_DueDate ON dbo.tProject(DueDate);
    END;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Create_tService
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID(N'dbo.tService', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.tService
        (
            ServiceID INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tService PRIMARY KEY,
            ServiceName VARCHAR(150) NOT NULL,
            ServiceDescription VARCHAR(255) NULL,
            ServiceCategoryID INT NOT NULL,
			FunctionID INT NOT NULL,
            StatusID INT NOT NULL,
            CreatedDate DATETIME2(0) NOT NULL CONSTRAINT DF_tService_CreatedDate DEFAULT (SYSUTCDATETIME()),
            ModifiedDate DATETIME2(0) NULL,
            IsDeleted BIT NOT NULL CONSTRAINT DF_tService_IsDeleted DEFAULT (0),
            DeletedDate DATETIME2(0) NULL,
            IsActive BIT NOT NULL CONSTRAINT DF_tService_IsActive DEFAULT (1),
            CONSTRAINT FK_tService_tServiceCategory FOREIGN KEY (ServiceCategoryID)
                REFERENCES dbo.tServiceCategory(ServiceCategoryID) ON DELETE NO ACTION,
            CONSTRAINT FK_tService_tStatus FOREIGN KEY (StatusID)
                REFERENCES dbo.tStatus(StatusID) ON DELETE NO ACTION
        );
        CREATE UNIQUE INDEX UQ_tService_ServiceName_Active ON dbo.tService(ServiceName) WHERE IsDeleted = 0;
        CREATE INDEX IX_tService_ServiceCategoryID ON dbo.tService(ServiceCategoryID);
        CREATE INDEX IX_tService_StatusID ON dbo.tService(StatusID);
    END;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Create_tProjectService
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID(N'dbo.tProjectService', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.tProjectService
        (
            ProjectID INT NOT NULL,
            ServiceID INT NOT NULL,
            Frequency VARCHAR(20) NULL,
            StartDate DATE NULL,
            EndDate DATE NULL,
            CreatedDate DATETIME2(0) NOT NULL CONSTRAINT DF_tProjectService_CreatedDate DEFAULT (SYSUTCDATETIME()),
            IsActive BIT NOT NULL CONSTRAINT DF_tProjectService_IsActive DEFAULT (1),
            CONSTRAINT PK_tProjectService PRIMARY KEY (ProjectID, ServiceID),
            CONSTRAINT FK_tProjectService_tProject FOREIGN KEY (ProjectID)
                REFERENCES dbo.tProject(ProjectID) ON DELETE CASCADE,
            CONSTRAINT FK_tProjectService_tService FOREIGN KEY (ServiceID)
                REFERENCES dbo.tService(ServiceID) ON DELETE NO ACTION
        );
        CREATE INDEX IX_tProjectService_ServiceID ON dbo.tProjectService(ServiceID);
    END;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Create_tServiceTag
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID(N'dbo.tServiceTag', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.tServiceTag
        (
            ServiceID INT NOT NULL,
            TagID INT NOT NULL,
            IsActive BIT NOT NULL CONSTRAINT DF_tServiceTag_IsActive DEFAULT (1),
            CONSTRAINT PK_tServiceTag PRIMARY KEY (ServiceID, TagID),
            CONSTRAINT FK_tServiceTag_tService FOREIGN KEY (ServiceID)
                REFERENCES dbo.tService(ServiceID) ON DELETE CASCADE,
            CONSTRAINT FK_tServiceTag_tTag FOREIGN KEY (TagID)
                REFERENCES dbo.tTag(TagID) ON DELETE NO ACTION
        );
        CREATE INDEX IX_tServiceTag_TagID ON dbo.tServiceTag(TagID);
    END;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Create_tProjectRevenue
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID(N'dbo.tProjectRevenue', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.tProjectRevenue
        (
            RevenueID INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tProjectRevenue PRIMARY KEY,
            ProjectID INT NOT NULL,
            EffectiveDate DATE NOT NULL,
            Revenue DECIMAL(18,2) NOT NULL,
            ChangeReason VARCHAR(255) NULL,
            RecordedBy INT NOT NULL,
            CreatedDate DATETIME2(0) NOT NULL CONSTRAINT DF_tProjectRevenue_CreatedDate DEFAULT (SYSUTCDATETIME()),
            IsActive BIT NOT NULL CONSTRAINT DF_tProjectRevenue_IsActive DEFAULT (1),
            CONSTRAINT FK_tProjectRevenue_tProject FOREIGN KEY (ProjectID)
                REFERENCES dbo.tProject(ProjectID) ON DELETE CASCADE,
            CONSTRAINT FK_tProjectRevenue_tUser_RecordedBy FOREIGN KEY (RecordedBy)
                REFERENCES dbo.tUser(UserID) ON DELETE NO ACTION
        );
        CREATE INDEX IX_tProjectRevenue_ProjectID_EffectiveDate ON dbo.tProjectRevenue(ProjectID, EffectiveDate DESC);
        CREATE INDEX IX_tProjectRevenue_RecordedBy ON dbo.tProjectRevenue(RecordedBy);
    END;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Create_tProjectNote
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID(N'dbo.tProjectNote', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.tProjectNote
        (
            NoteID INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tProjectNote PRIMARY KEY,
            ProjectID INT NOT NULL,
            UserID INT NOT NULL,
            NoteDate DATETIME2(0) NOT NULL CONSTRAINT DF_tProjectNote_NoteDate DEFAULT (SYSUTCDATETIME()),
            NoteType VARCHAR(30) NOT NULL,
            NoteText NVARCHAR(MAX) NOT NULL,
            IsPinned BIT NOT NULL CONSTRAINT DF_tProjectNote_IsPinned DEFAULT (0),
            CreatedDate DATETIME2(0) NOT NULL CONSTRAINT DF_tProjectNote_CreatedDate DEFAULT (SYSUTCDATETIME()),
            ModifiedDate DATETIME2(0) NULL,
            IsActive BIT NOT NULL CONSTRAINT DF_tProjectNote_IsActive DEFAULT (1),
            CONSTRAINT FK_tProjectNote_tProject FOREIGN KEY (ProjectID)
                REFERENCES dbo.tProject(ProjectID) ON DELETE CASCADE,
            CONSTRAINT FK_tProjectNote_tUser FOREIGN KEY (UserID)
                REFERENCES dbo.tUser(UserID) ON DELETE NO ACTION
        );
        CREATE INDEX IX_tProjectNote_ProjectID_NoteDate ON dbo.tProjectNote(ProjectID, IsPinned DESC, NoteDate DESC);
        CREATE INDEX IX_tProjectNote_UserID ON dbo.tProjectNote(UserID);
    END;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Create_tActivityLog
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID(N'dbo.tActivityLog', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.tActivityLog
        (
            LogID BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tActivityLog PRIMARY KEY,
            EntityType VARCHAR(50) NOT NULL,
            EntityID INT NOT NULL,
            ActionDate DATETIME2(0) NOT NULL CONSTRAINT DF_tActivityLog_ActionDate DEFAULT (SYSUTCDATETIME()),
            Action VARCHAR(30) NOT NULL,
            FieldName VARCHAR(100) NULL,
            OldValue NVARCHAR(MAX) NULL,
            NewValue NVARCHAR(MAX) NULL,
            IsActive BIT NOT NULL CONSTRAINT DF_tActivityLog_IsActive DEFAULT (1)
        );
        CREATE INDEX IX_tActivityLog_Entity ON dbo.tActivityLog(EntityType, EntityID, ActionDate DESC);
    END;
END;
GO


/* ============================================================
   Execute table creation stored procedures
   ============================================================ */
EXEC dbo.usp_Create_tBusinessUnit;
GO

EXEC dbo.usp_Create_tBusinessClass;
GO
EXEC dbo.usp_Create_tBusinessClassTag;
GO

EXEC dbo.usp_Create_tClient;
GO
--EXEC dbo.usp_Create_tClientCategory;
--GO
--EXEC dbo.usp_Create_tClientType;
--GO

EXEC dbo.usp_Create_tProject;
GO
EXEC dbo.usp_Create_tProjectType;
GO
EXEC dbo.usp_Create_tProjectRevenue;
GO
EXEC dbo.usp_Create_tProjectNote;
GO

EXEC dbo.usp_Create_tStatusType;
GO
EXEC dbo.usp_Create_tStatus;
GO
EXEC dbo.usp_Create_tStatusAppliesTo;
GO

EXEC dbo.usp_Create_tService;
GO
EXEC dbo.usp_Create_tServiceCategory;
GO
EXEC dbo.usp_Create_tProjectService;
GO
EXEC dbo.usp_Create_tServiceTag;
GO

EXEC dbo.usp_Create_tUser;
GO
EXEC dbo.usp_Create_tUserRole;
GO
EXEC dbo.usp_Create_tUserAddress;
GO
EXEC dbo.usp_Create_tUserGroup;
GO
EXEC dbo.usp_Create_tUserGroupUser;
GO

EXEC dbo.usp_Create_tTag;
GO
EXEC dbo.usp_Create_tActivityLog;
GO



SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

CREATE OR ALTER VIEW dbo.vClient
AS
SELECT
    c.*,
    s.StatusDescription AS ClientStatus
FROM dbo.tClient c
JOIN dbo.tStatus s ON c.StatusID = s.StatusID
WHERE c.IsDeleted = 0;
GO
CREATE OR ALTER VIEW dbo.vClient_WithBU
AS
SELECT
    c.ClientID,
    c.ClientName,
    c.ClientShortName,
	c.MGA,
	c.Program,
    bu.BusinessUnitID,
    bu.BusinessUnitName,
    bc.BusinessClassID,
    bc.BusinessClassName,
    s.StatusID,
    s.StatusCode,
    s.StatusDescription,
    c.CreatedDate,
    c.ModifiedDate
FROM dbo.vClient c
JOIN dbo.tBusinessClass bc ON c.BusinessClassID = bc.BusinessClassID
JOIN dbo.tBusinessUnit bu ON bc.BusinessUnitID = bu.BusinessUnitID
JOIN dbo.tStatus s ON c.StatusID = s.StatusID;
GO

CREATE OR ALTER VIEW dbo.vProject
AS
SELECT
    p.*,
    s.StatusDescription AS ProjectStatus
FROM dbo.tProject p
JOIN dbo.tStatus s ON p.StatusID = s.StatusID
WHERE p.IsDeleted = 0;
GO
CREATE OR ALTER VIEW dbo.vProject_Current
AS
SELECT
    bu.BusinessUnitName,
    bc.BusinessClassName,
    p.ClientID,
    c.ClientName,
    c.ClientStatus,
    p.ProjectID,
    p.ProjectNumber,
    p.ProjectName,
    p.ProjectTypeID,
    pt.PTCode,
    pt.PTDescription,
    p.UGID,
    ug.UGName,
    p.RenewalDate,
    p.DueDate,
    p.ProjectStatus,
    lr.RevenueID AS LatestRevenueID,
    lr.EffectiveDate AS LatestRevenueEffectiveDate,
    lr.Revenue AS LatestRevenue,
    p.CreatedDate,
    p.ModifiedDate
FROM dbo.vProject p
JOIN dbo.vClient c ON p.ClientID = c.ClientID
JOIN dbo.tBusinessClass bc ON c.BusinessClassID = bc.BusinessClassID
JOIN dbo.tBusinessUnit bu ON bc.BusinessUnitID = bu.BusinessUnitID
JOIN dbo.vUserGroup ug ON p.UGID = ug.UGID
JOIN dbo.tProjectType pt ON p.ProjectTypeID = pt.ProjectTypeID
OUTER APPLY
(
    SELECT TOP (1) pr.*
    FROM dbo.tProjectRevenue pr
    WHERE pr.ProjectID = p.ProjectID
    ORDER BY pr.EffectiveDate DESC, pr.RevenueID DESC
) lr;
GO

CREATE OR ALTER VIEW dbo.vUser
AS
SELECT *
FROM dbo.tUser
WHERE IsDeleted = 0;
GO
CREATE OR ALTER VIEW dbo.vUserGroup
AS
SELECT *
FROM dbo.tUserGroup
WHERE IsDeleted = 0;
GO

CREATE OR ALTER VIEW dbo.vService
AS
SELECT *
FROM dbo.tService
WHERE IsDeleted = 0;
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/*============================================
Seed data from the Seed Data workbook tab. 
=============================================*/
SET IDENTITY_INSERT dbo.tBusinessUnit ON;

MERGE dbo.tBusinessUnit AS tgt
USING (VALUES
    (1, 'US', 'U.S. clients'),
    (2, 'UK', 'U.K. clients'),
    (3, 'LA', 'L.A. clients')
) AS src(BusinessUnitID, BusinessUnitName, BusinessUnitDescription)
ON tgt.BusinessUnitID = src.BusinessUnitID
WHEN MATCHED THEN
    UPDATE SET
        tgt.BusinessUnitName = src.BusinessUnitName,
        tgt.BusinessUnitDescription = src.BusinessUnitDescription,
        tgt.IsActive = 1
WHEN NOT MATCHED THEN
    INSERT (BusinessUnitID, BusinessUnitName, BusinessUnitDescription, IsActive)
    VALUES (src.BusinessUnitID, src.BusinessUnitName, src.BusinessUnitDescription, 1);

SET IDENTITY_INSERT dbo.tBusinessUnit OFF;
GO

SET IDENTITY_INSERT dbo.tBusinessClass ON;
MERGE dbo.tBusinessClass AS tgt
USING (VALUES
    (1,  'Re',     'Reinsurance',              1),
    (2,  'NM Re',  'Non-Marine Retro',         2),
    (3,  'NA DF',  'North American D&F',       2),
    (4,  'Int DF', 'International D&F',        3),
    (5,  'DUA',    'Delegated Authority',      2),
    (6,  'EN',     'Energy',                   2),
    (7,  'MA',     'Marine',                   2),
    (8,  'SP Re',  'Specialty Re',             2),
    (9,  'NZ',     'BMS Risk NZ',              2),
    (10, 'BE',     'Bermuda',                  2),
    (11, 'TR',     'Turkey',                   2),
    (12, 'PS',     'Portfolio Solutions',      2)
) AS src(BusinessClassID, BusinessClassName, BusinessClassDescription, BusinessUnitID)
ON tgt.BusinessClassID = src.BusinessClassID
WHEN MATCHED THEN
    UPDATE SET
        tgt.BusinessClassName = src.BusinessClassName,
        tgt.BusinessClassDescription = src.BusinessClassDescription,
        tgt.BusinessUnitID = src.BusinessUnitID,
        tgt.IsActive = 1
WHEN NOT MATCHED THEN
    INSERT (BusinessClassID, BusinessClassName, BusinessClassDescription, BusinessUnitID, IsActive)
    VALUES (src.BusinessClassID, src.BusinessClassName, src.BusinessClassDescription, src.BusinessUnitID, 1);
	SET IDENTITY_INSERT dbo.tBusinessClass OFF;
GO

SET IDENTITY_INSERT dbo.tClient ON;
MERGE dbo.tClient AS tgt
USING
(
    VALUES
        ('Branch', 'Bra', NULL, 'Everspan', 1, 2),
        ('Brotherhood', 'Bro', NULL, 'Property', 1, 1),
        ('Gulf States', 'GSIC', NULL, NULL, 1, 1),
        ('Triangle', 'Tri', NULL, NULL, 1, 1),
        ('Great American', 'Gre', NULL, 'Public Sector', 1, 1),
        ('AmTrust', 'AmT', NULL, 'WC', 1, 2),
        ('Amtrust', 'Amt', NULL, 'Property', 1, 2),
        ('Brethren', 'Bre', NULL, NULL, 1, 1),
        ('Insurate', 'Ins', NULL, 'Workers Comp', 1, 1),
        ('Hylant', 'Hyl', NULL, 'OSP', 1, 1),
        ('Rockingham Mutual', 'Roc', NULL, NULL, 1, 1),
        ('Brotherhood', 'Bro', NULL, 'WC', 1, 2),
        ('Cable Holdings', 'Cab', NULL, NULL, 1, 1),
        ('FMIT', 'FMI', 'EPIC', 'Renewal', 1, 1),
        ('Covenant Underwriters Hotel Program', 'Cov', NULL, NULL, 1, 1),
        ('Northern Mutual', 'Nor', NULL, NULL, 1, 1),
        ('Hylant', 'Hyl', NULL, 'OPRM', 1, 1),
        ('ISC Property', 'ISC', NULL, NULL, 1, 1),
        ('US Lloyds', 'US', NULL, NULL, 1, 1),
        ('Mutual Benefit', 'Mut', NULL, 'Cat', 1, 1),
        ('Lucid Ins Group', 'Luc', 'Lucid', 'AgRisk', 1, 1),
        ('Accident Fund', 'Acc', 'PBC', 'Brownstone Hab - Prosight', 1, 2),
        ('ARECA Insurance Exchange', 'ARE', NULL, NULL, 1, 1),
        ('Kensington', 'Ken', NULL, 'Property', 1, 2),
        ('ITAU SEGUROS', 'ITA', NULL, 'Reinsurance', 4, 1),
        ('Inbursa Mexico', 'Inb', NULL, 'Reinsurance', 4, 1),
        ('Amerisure', 'Ame', 'Arden', 'MI, WI', 1, 2),
        ('Internacional de Panama', 'Int', NULL, 'Reinsurance', 4, 1),
        ('CoverTree', 'Cov', NULL, 'MH WC', 1, 2),
        ('Falls Lake', 'Fal', 'Paragon', 'Auto Dealers', 1, 1),
        ('Obsidian', 'Obs', 'Paragon', 'Timber - Property', 1, 1),
        ('Vikco Insurance Services', 'Vik', NULL, NULL, 1, 2),
        ('Safepoint Ins', 'Saf', 'Theodore Tunick & Co.', 'Property - USVI', 1, 2),
        ('Agency Insurance Company of Maryland, Inc.', 'Age', NULL, NULL, 1, 1),
        ('Obsidian', 'Obs', 'Deans and Homer', NULL, 1, 1),
        ('LA CEIBA', 'LA', NULL, 'Reinsurance', 4, 1),
        ('League of Minnesota Cities (LMCIT)', 'Lea', NULL, 'PE: Prop, Terror, Cyber', 1, 1),
        ('NEC', 'NEC', 'NEC', 'Headron', 1, 1),
        ('SiriusPoint', 'Sir', 'Insurate', 'Workers Comp', 1, 2),
        ('Home State County Mutual', 'Hom', 'Apollo Uws', NULL, 1, 1),
        ('CFM/Cornerstone', 'CFM', NULL, NULL, 1, 2),
        ('Chard Re', 'Cha', NULL, 'Property Retro', 4, 3),
        ('First Benefits Ins Mutual', 'Fir', NULL, NULL, 1, 2),
        ('VA/WV Corp', 'VA/', NULL, NULL, 1, 1),
        ('Clearcover', 'Cle', NULL, NULL, 1, 1),
        ('Fortegra', 'For', 'UCA General', 'DIC (SL, FF)', 1, 2),
        ('American Mobile Ins Exchange', 'Ame', 'K2', 'FL MH', 1, 2),
        ('IAT Insurance Group', 'IAT', 'Harco', 'SBOA', 1, 1),
        ('National Fire & Indemnity', 'Nat', NULL, NULL, 1, 1),
        ('Transverse', 'Tra', 'Foresight', 'WC', 1, 2),
        ('Palomar', 'Pal', 'Cabrillo', 'Atlantic Coastal', 1, 2),
        ('Healthcare Providers Ins Co', 'Hea', NULL, 'Workers Comp', 1, 1),
        ('Arch / Argo', 'Arc', 'Paragon', 'Public Entity Non NE States', 1, 1),
        ('Planters', 'Pla', NULL, NULL, 1, 1),
        ('Arch / Argo', 'Arc', 'Paragon', 'Public Entity NE States', 1, 1),
        ('Honeycomb', 'Hon', NULL, NULL, 1, 2),
        ('Mount Morris', 'Mou', NULL, NULL, 1, 2),
        ('Allianz Seguros Mexico', 'All', NULL, 'Reinsurance', 4, 1),
        ('Banorte', 'Ban', NULL, 'Reinsurance', 4, 1),
        ('Vishay', 'Vis', NULL, 'D&F Client', 4, 1),
        ('Lloyd''s 0510 - TMK KLN', 'Llo', NULL, 'Cat on D&F - US, CAD, CB', 4, 1),
        ('Progressive', 'Pro', NULL, 'BOP', 1, 2),
        ('Joyn Insurance', 'Joy', NULL, 'Property', 1, 2),
        ('ARK Bermuda', 'ARK', NULL, 'Property Retro', 4, 3),
        ('McMillan Warner', 'McM', NULL, NULL, 1, 2),
        ('Wisconsin Education Assoc', 'Wis', NULL, 'HO + APD', 1, 2),
        ('Cypress P&C', 'Cyp', 'Vista Re', 'Florida', 1, 2),
        ('Conifer Holdings', 'Con', NULL, 'Programs', 1, 2),
        ('Comedica', 'Com', NULL, 'Reinsurance', 4, 1),
        ('Core Specialty (fka: Star Stone)', 'Cor', 'Atlas', 'Ag WC', 1, 2),
        ('Accredited America', 'Acc', 'Atlas II', 'Atlas II', 1, 2),
        ('Palms Insurance Company', 'Pal', 'Insurisk', 'Sp[ectrum Facility', 1, 2),
        ('Project Trident', 'Pro', NULL, 'Citizens DePop', 1, 2),
        ('Benchmark', 'Ben', 'Coterie', 'BOP', 1, 2),
        ('Ryan Specialty Group', 'Rya', NULL, 'Cat Bond', 1, 2),
        ('Applied U/W', 'App', NULL, 'Property + WC', 1, 2),
        ('Accident Fund', 'Acc', 'PBC', 'Brownstone Hab - Sutton', 1, 2),
        ('Centauri', 'Cen', NULL, NULL, 1, 2),
        ('Cypress Texas Ins Co', 'Cyp', 'Vista Re', 'Texas', 1, 2),
        ('Davidson County Mutual', 'Dav', NULL, NULL, 1, 2),
        ('Everett Cash Mutual', 'Eve', NULL, 'Farm, Ranch, Equine', 1, 2),
        ('HDVI', 'HDV', NULL, NULL, 1, 2),
        ('IAT Insurance Group', 'IAT', 'SageSure', 'Safe Port Wildfire', 1, 2),
        ('IAT Insurance Group', 'IAT', NULL, 'Safepoint?', 1, 2),
        ('IAT Insurance Group', 'IAT', NULL, 'Base?', 1, 2),
        ('Old American County Mutual', 'Old', 'Connect', 'APD', 1, 2),
        ('Starwind Specialty Insurance', 'Sta', NULL, 'Prop + Liab', 1, 2),
        ('Program Partners', 'Pro', 'Program Partners', 'Cover Combo', 1, 2),
        ('Heartland Mutual', 'Hea', NULL, 'Farm Program', 1, 2),
        ('Incline Casualty Company', 'Inc', 'Lead General Agency', 'Property', 1, 2),
        ('One80/EE Hall', 'One', 'One80 Intermediaries', 'Property', 1, 2),
        ('Costa Palmas', 'Cos', NULL, 'Property', 1, 2),
        ('Fairmont Farmers', 'Fai', NULL, NULL, 1, 2),
        ('Affinity Mutual Insurance Company', 'Aff', NULL, NULL, 1, 2),
        ('Agromercantil', 'Agr', NULL, 'Reinsurance', 4, 1),
        ('Atlas', 'Atl', NULL, 'Reinsurance', 4, 3),
        ('Cardif Seguros', 'Car', NULL, 'Reinsurance', 4, 1),
        ('Fedecredito El Salvador', 'Fed', NULL, 'Reinsurance', 4, 1),
        ('GMX', 'GMX', NULL, 'Reinsurance', 4, 1),
        ('GNP Mexico', 'GNP', NULL, 'Reinsurance', 4, 3),
        ('Great America', 'Gre', NULL, 'Reinsurance', 4, 3),
        ('GT', 'GT', NULL, 'Reinsurance', 4, 1),
        ('Insurance Corp of Belize Ltd (ICB)', 'Ins', NULL, 'Reinsurance', 4, 1),
        ('INISER', 'INI', NULL, 'Reinsurance', 4, 1),
        ('LA CENTRAL DE SEGUROS Y FIANZAS', 'LA', NULL, 'Reinsurance', 4, 1),
        ('Puebla (Fonden)', 'Pue', NULL, 'Reinsurance', 4, 1),
        ('SEGUROS CREFISA', 'SEG', NULL, 'Reinsurance', 4, 1),
        ('SEGUROS DEL PACIFICO', 'SEG', NULL, 'Reinsurance', 4, 1),
        ('SEGUROS LAFISE', 'SEG', NULL, 'Reinsurance', 4, 1),
        ('SISA', 'SIS', NULL, 'Reinsurance', 4, 1),
        ('CCUW', 'CCU', NULL, 'Reinsurance', 4, 1),
        ('Aliado', 'Ali', NULL, 'Reinsurance', 4, 1),
        ('Optima', 'Opt', NULL, NULL, 4, 1),
        ('Great American', 'Gre', NULL, 'Alternative Markets', 1, 1),
        ('Players Health', 'Pla', NULL, 'Property', 1, 3),
        ('Specialty E&S', 'Spe', 'Paragon', 'Specialty E&S', 1, 1),
        ('Risksmith', 'Ris', NULL, 'Excess All', 1, 2),
        ('Risksmith', 'Ris', NULL, 'Excess Sutton Only', 1, 2),
        ('RiskSmith AXA XL', 'Ris', NULL, 'Whole Account QS', 1, 2),
        ('RiskSmith Cincinnati Re', 'Ris', NULL, 'E&S + BR QS', 1, 2),
        ('Transverse', 'Tra', 'GSR', 'Agri Business', 1, 2),
        ('WorkFirst Casualty Company', 'Wor', NULL, 'Workers Comp', 1, 2),
        ('Fortegra - SHU', 'For', 'SHU - looking for Carrier', 'Property Hotel / Motel in TX', 1, 2),
        ('AXIS Insurance', 'AXI', NULL, 'SkiSafe Program', 1, 2),
        ('Alliant REI', 'All', 'Alliant', 'REI', 1, 2),
        ('Allied Trust', 'All', NULL, 'Homeowners', 1, 2),
        ('Aegis General Insurance Agency, Inc.', 'Aeg', 'K2 International', 'HO / MHO', 1, 2),
        ('Arch Re Fac', 'Arc', NULL, 'Matching Lines Facility', 1, 2),
        ('Waypoint Mutual (FKA Midwest Builders)', 'Way', NULL, NULL, 1, 2),
        ('Safehold Special Risk', 'Saf', NULL, 'Property', 1, 2),
        ('Obsidian', 'Obs', 'Paragon', 'Timber - Inland Marine', 1, 1),
        ('Incline Casualty Company', 'Inc', 'Paramount', 'Commercial Auto', 1, 1),
        ('Incline Casualty Company', 'Inc', 'Strategic Ins UW', 'Commercial Auto', 1, 1),
        ('Hinterland', 'Hin', 'FTP of CA', 'Property', 1, 1),
        ('Leatherstocking Cooperative Insurance Company', 'Lea', NULL, NULL, 1, 1),
        ('Trisura', 'Tri', 'Coastal', NULL, 1, 2),
        ('US Coastal P&C', 'US', 'Cabrillo', NULL, 1, 2),
        ('Vikco Insurance Services', 'Vik', NULL, 'Habitational', 1, 2),
        ('Transverse', 'Tra', 'CondoLogic', 'Commercial Hab', 1, 2),
        ('Dale Underwriters', 'Dal', NULL, 'Property', 4, 2),
        ('FSBIT', 'FSB', NULL, 'Property - CWIL', 4, 3),
        ('Lloyd''s 0510 - TMK KLN', 'Llo', NULL, 'Cat on D&F - Caribbean', 4, 1),
        ('Lloyd''s 1183 - Talbot AIG', 'Llo', NULL, 'Cat on D&F', 4, 1),
        ('Lloyd''s 1274 - Antares', 'Llo', NULL, 'Property Treaty SPA', 4, 1),
        ('Lloyd''s 1739 - Dale UWs', 'Llo', NULL, 'Property Retro QS', 4, 3),
        ('Lloyd''s 1969 - Apollo', 'Llo', NULL, 'Cat on D&F', 4, 3),
        ('Lloyd''s 2001 - MSA', 'Llo', NULL, 'Property Retro QS', 4, 1),
        ('Lloyd''s 2001 - MSA', 'Llo', NULL, 'Cat on D&F', 4, 1),
        ('Lloyd''s 4020 - ARK', 'Llo', NULL, 'Retro', 4, 3),
        ('Lloyd''s 4141 - TMK HCC', 'Llo', NULL, 'Retro', 4, 1),
        ('Lloyd''s 4141 - TMK HCC', 'Llo', NULL, 'Property Retro - WW', 4, 1),
        ('Lloyd''s 4444 - Canopius', 'Llo', NULL, 'Retro QS', 4, 1),
        ('Lloyd''s 5886 - Blenheim', 'Llo', NULL, 'Cat on D&F', 4, 1),
        ('Nephlia', 'Nep', NULL, 'Property Retro', 4, 3),
        ('RenRe', 'Ren', NULL, 'Property Retro', 4, 3),
        ('South Shore Insurance UW', 'Sou', NULL, NULL, 4, 1),
        ('Fidelis', 'Fid', NULL, 'Cat XL', 4, 1),
        ('TMK', 'TMK', NULL, 'CB Carve Out Cat XL', 4, 1),
        ('TMK', 'TMK', NULL, 'AU, NZ, TW Carve Out Cat XL', 4, 1),
        ('Apollo Consortium', 'Apo', NULL, 'FAC: Consortium', 5, 1),
        ('APIP', 'API', NULL, NULL, 8, 1),
        ('Waldorf', 'Wal', NULL, NULL, 8, 1),
        ('Lions Gate insurance', 'Lio', NULL, NULL, 8, 1),
        ('Americold', 'Ame', NULL, NULL, 8, 1),
        ('Risco', 'Ris', NULL, NULL, 8, 1),
        ('Property Ins Cormpany of Amer. (PICA)', 'Pro', 'Cajun Underwriters', 'HO', 1, 2),
        ('Ocean Marine Indemnity', 'Oce', 'Gulf Coast Marine', 'Marine Hull', 1, 2),
        ('Inigo', 'Ini', NULL, 'PVT - War', 1, 2),
        ('SCIRF', 'SCI', NULL, 'Property', 1, 2),
        ('Talbot Underwriting', 'Tal', NULL, 'Property', 1, 2),
        ('Aldgate', 'Ald', NULL, NULL, 1, 2),
        ('Insure-Fi', 'Ins', NULL, 'Property', 1, 2),
        ('Lloyds Various', 'Llo', 'Balance Partners', 'WDBB', 1, 2),
        ('MiniCo', 'Min', NULL, NULL, 1, 2),
        ('State of Georgia', 'Sta', NULL, 'Property CWIL', 1, 2),
        ('Trisura', 'Tri', 'Balance Partners', 'Pentium', 1, 2),
        ('WKFC Underwriting Managers', 'WKF', NULL, NULL, 1, 2),
        ('Frontline Insurance', 'Fro', NULL, 'Top Layer Cat XOL', 1, 2),
        ('Ryan Specialty Group', 'Rya', NULL, 'Sidecar', 1, 2),
        ('Citadel Insurance Svcs', 'Cit', 'Citadel Insurance Svcs', 'REI', 1, 2),
        ('Cap Advisory - Cabrillo', 'Cap', NULL, 'Cat Bond', 1, 2),
        ('Cap Advisory _Bamboo', 'Cap', NULL, 'Debt Financing', 1, 2),
        ('Amtrust', 'Amt', 'Summit', 'WC', 1, 2),
        ('Cap Advisory - Westfield', 'Cap', NULL, 'Sidecar', 1, 2),
        ('Cap Advisory - Peoples Trust', 'Cap', NULL, 'Cat Bond', 1, 2),
        ('Cap Advisory_TripleAAA', 'Cap', NULL, 'Cat Bond', 1, 2),
        ('Specialty E&S - Wildfire', 'Spe', 'Paragon', 'Wildfire Comercial Residential', 1, 2),
        ('The Philadelphia Contributionship', 'The', NULL, NULL, 1, 2),
        ('Mennonite Mutual', 'Men', NULL, NULL, 1, 3),
        ('ISC WC', 'ISC', NULL, NULL, 1, 1),
        ('Steadpoint', 'Ste', NULL, NULL, 1, 2),
        ('Verita/ Verist', 'Ver', NULL, NULL, 1, 2),
        ('Westfield Ins Co', 'Wes', NULL, NULL, 1, 2),
        ('Branch', 'Bra', NULL, 'BIX', 1, 2),
        ('Aseguradora Guatemalteca', 'Ase', NULL, 'Reinsurance', 4, 3),
        ('Aseguradora Popular', 'Ase', NULL, 'Reinsurance', 4, 1),
        ('Seguros BX+ Mexico Ve Por Mas', 'Seg', NULL, 'Reinsurance', 4, 1),
        ('AmTrust', 'AmT', NULL, 'Specialty Prog QS', 1, 2),
        ('Pharmacists Mutual', 'Pha', NULL, NULL, 1, 2),
        ('Falls Lake', 'Fal', 'Atlas', 'WC', 1, 2),
        ('Arrowhead', 'Arr', 'Ascot', 'DIC', 1, 2),
        ('US Coastal Ins. Co.', 'US', 'Cabrillo', NULL, 1, 2),
        ('IAT Insurance Group', 'IAT', 'Alchemy', 'SBOA', 1, 3),
        ('Alliant', 'All', NULL, NULL, 1, 3)
) AS src(ClientName, ClientShortName, MGA, Program, BusinessClassID, StatusID)
ON  tgt.ClientName = src.ClientName
AND ISNULL(tgt.MGA, '') = ISNULL(src.MGA, '')
AND ISNULL(tgt.Program, '') = ISNULL(src.Program, '')
AND tgt.BusinessClassID = src.BusinessClassID
AND tgt.IsDeleted = 0
WHEN MATCHED THEN
    UPDATE SET
        tgt.ClientShortName = src.ClientShortName,
        tgt.MGA = src.MGA,
        tgt.Program = src.Program,
        tgt.BusinessClassID = src.BusinessClassID,
        tgt.StatusID = src.StatusID,
        tgt.ModifiedDate = SYSUTCDATETIME()
WHEN NOT MATCHED THEN
    INSERT
    (
        ClientName,
        ClientShortName,
        MGA,
        Program,
        BusinessClassID,
        StatusID
    )
    VALUES
    (
        src.ClientName,
        src.ClientShortName,
        src.MGA,
        src.Program,
        src.BusinessClassID,
        src.StatusID
    );
GO

SET IDENTITY_INSERT dbo.tStatusType ON;
MERGE dbo.tStatusType AS tgt
USING (VALUES
    (1, 'Universal', 1),
    (2, 'Client', 1),
    (3, 'Project', 1),
    (4, 'UserGroup', 1),
    (5, 'User', 1),
    (6, 'Service', 1)
) AS src(StatusTypeID, TypeName, IsActive)
ON tgt.StatusTypeID = src.StatusTypeID
WHEN MATCHED THEN UPDATE SET TypeName = src.TypeName, IsActive = src.IsActive
WHEN NOT MATCHED THEN INSERT (StatusTypeID, TypeName, IsActive) VALUES (src.StatusTypeID, src.TypeName, src.IsActive);
SET IDENTITY_INSERT dbo.tStatusType OFF;
GO

SET IDENTITY_INSERT dbo.tStatus ON;
MERGE dbo.tStatus AS tgt
USING (VALUES
    (1, 1, 'A', 'Active', 1),
    (2, 1, 'I', 'Inactive', 1),
    (3, 2, 'P', 'Prospect', 1),
    (4, 3, 'S', 'Started', 1),
    (5, 3, 'C', 'Complete', 1),
    (6, 1, 'U', 'Unknown', 1)
) AS src(StatusID, StatusTypeID, StatusCode, StatusDescription, IsActive)
ON tgt.StatusID = src.StatusID
WHEN MATCHED THEN UPDATE SET
    StatusTypeID = src.StatusTypeID,
    StatusCode = src.StatusCode,
    StatusDescription = src.StatusDescription,
    IsActive = src.IsActive
WHEN NOT MATCHED THEN
    INSERT (StatusID, StatusTypeID, StatusCode, StatusDescription, IsActive)
    VALUES (src.StatusID, src.StatusTypeID, src.StatusCode, src.StatusDescription, src.IsActive);
SET IDENTITY_INSERT dbo.tStatus OFF;
GO

MERGE dbo.tStatusAppliesTo AS tgt
USING (VALUES
    (1, 'Universal', 'All entities'),
    (2, 'Universal', 'All entities'),
    (6, 'Universal', 'All entities fallback'),
    (3, 'Client', 'Client-only'),
    (4, 'Project', 'Project-only'),
    (5, 'Project', 'Project-only')
) AS src(StatusID, EntityType, Notes)
ON tgt.StatusID = src.StatusID AND tgt.EntityType = src.EntityType
WHEN MATCHED THEN UPDATE SET Notes = src.Notes
WHEN NOT MATCHED THEN INSERT (StatusID, EntityType, Notes) VALUES (src.StatusID, src.EntityType, src.Notes);
GO

--MERGE dbo.tClientCategory AS tgt
--USING (VALUES
--        (N'PIC', N'Primary Insurance Carrier'),
--        (N'MGA', N'Managing General Agent'),
--        (N'FC', N'Fronting Carriers'),
--        (N'RRG', N'Risk Retention Groups'),
--        (N'RM', N'Residual Market - Govt')
--) AS src(CCName, CCDescription)
--ON tgt.CCName = src.CCName
--WHEN MATCHED THEN UPDATE SET CCDescription = src.CCDescription, IsActive = 1
--WHEN NOT MATCHED THEN INSERT (CCName, CCDescription, IsActive) VALUES (src.CCName, src.CCDescription, 1);
--GO

--MERGE dbo.tClientType AS tgt
--USING (VALUES
--    ('Carrier', 'Insurance carrier'),
--    ('Broker', 'Broker or intermediary'),
--    ('Reinsurer', 'Reinsurance entity'),
--    ('Corporate', 'Corporate client')
--) AS src(CTName, CTDescription)
--ON tgt.CTName = src.CTName
--WHEN MATCHED THEN UPDATE SET CTDescription = src.CTDescription, IsActive = 1
--WHEN NOT MATCHED THEN INSERT (CTName, CTDescription, IsActive) VALUES (src.CTName, src.CTDescription, 1);
--GO

MERGE dbo.tProjectType AS tgt
USING (VALUES
    ('RNWL', 'Renewal'),
    ('NB', 'New Business'),
    ('ADHOC', 'Ad hoc analysis'),
    ('Pro', 'Proforma')
) AS src(PTCode, PTDescription)
ON tgt.PTCode = src.PTCode
WHEN MATCHED THEN UPDATE SET PTDescription = src.PTDescription, IsActive = 1
WHEN NOT MATCHED THEN INSERT (PTCode, PTDescription, IsActive) VALUES (src.PTCode, src.PTDescription, 1);
GO

MERGE dbo.tServiceCategory AS tgt
USING (VALUES
    ('Exposure Management', 'Standard Reinsurance Placement'),
    ('Catastrophe Modeling', 'Standard Reinsurance Placement'),
	('Reinsurance Placement', 'Standard Reinsurance Placement'),
	('Model / Peril Advisory', 'Advisory'),
	('Portfolio Strategy', 'Advisory'),
	('Regulatory', 'Advisory')
) AS src(CategoryName, CatFunction)
ON tgt.CategoryName = src.CategoryName
WHEN MATCHED THEN UPDATE SET CatFunction = src.CatFunction, IsActive = 1
WHEN NOT MATCHED THEN INSERT (CategoryName, CatFunction, IsActive) VALUES (src.CategoryName, src.CatFunction, 1);
GO

MERGE dbo.tUserRole AS tgt
USING (VALUES
        ('Analytics Lead', 'Oversees all analytics for client'),
        ('Analyst I', 'Lead Analyst'),
		('Analyst II', 'Support Analyst'),
        ('Account Executive', 'Relationship Manager'),
        ('Broker', 'Lead Broker'),
        ('Broker II', 'Support Broker'),
		('Broker III', 'Support Broker')
) AS src(UserRoleName, RoleDescription)
ON tgt.UserRoleName = src.UserRoleName
WHEN MATCHED THEN UPDATE SET RoleDescription = src.RoleDescription, IsActive = 1
WHEN NOT MATCHED THEN INSERT (UserRoleName, RoleDescription, IsActive) VALUES (src.UserRoleName, src.RoleDescription, 1);
GO

SET IDENTITY_INSERT dbo.tUser ON;
MERGE dbo.tUser AS tgt
USING (VALUES
        (1, 'Scott', 'Christian', 'EVP, Castrophe Analytics & Advisory', 'Scott.Christian@bmsgroup.com', '817-856-9797', 'BMS', 'CAT', 'Internal', 1),
        (2, 'Andrew', 'Siffert', 'SVP, Global Head of Peril Adviosry', 'andrew.siffert@bmsgroup.com', '952-252-0812', 'BMS', 'CAT', 'Internal', 1),
        (3, 'Divya', 'Anand', 'VP, Catastrophe Analytics & Advisory', 'Divya.Anand@bmsgroup.com', '256-836-1194', 'BMS', 'CAT', 'Internal', 1),
        (4, 'Ryan', 'Vesledahl', 'SVP, Head of Cat Product', 'Ryan.Vesledahl@bmsgroup.com', '612-479-5480', 'BMS', 'CAT', 'Internal', 1),
        (5, 'Jason', 'Chou', 'VP, Catastrophe Analytics & Advisory', 'Jason.Chou@bmsgroup.com', '781-493-2671', 'BMS', 'CAT', 'Internal', 1),
        (6, 'James', 'Rogers', 'VP, Catastrophe Analytics & Advisory', 'James.Rogers@bmsgroup.com', '224 291 5586', 'BMS', 'CAT', 'Internal', 1),
        (7, 'Mitch', 'Von Hoffmann', 'VP, Catastrophe Analytics & Advisory', 'Mitch.VonHoffmann@bmsgroup.com', NULL, 'BMS', 'CAT', 'Internal', 1),
        (8, 'Jasmine', 'Weekes', 'Analyst', 'Jasmine.Weekes@bmsgroup.com', NULL, 'BMS', 'CAT', 'Internal', 1),
        (9, 'Shafaath', 'Qureshi', 'VP - Senior Principal Architect', 'Shafaath.Qureshi@bmsgroup.com', '908-432-1609', 'BMS', 'IT', 'Internal', 1),
        (10, 'Nitesh', 'Singh', 'Team Lead', 'Nitesh.Singh@bmsgroup.com', NULL, 'BMS', 'CAT', 'Internal', 1),
        (11, 'Anish', 'Singh', 'Sr. Specialist', 'Anish.Singh@bmsgroup.com', NULL, 'BMS', 'CAT', 'Internal', 1),
        (12, 'Akash', 'Patil', 'Sr. Specialist', 'Akash.Patil@bmsgroup.com', NULL, 'BMS', 'CAT', 'Internal', 1),
        (13, 'Kenneth', 'Bond', 'Analyst', 'chaz.bond@bmsgroup.com', NULL, 'BMS', 'Capital Advisory', 'Internal', 1),
        (14, 'Jack', 'Long', 'Analyst', 'jack.long@bmsgroup.com', NULL, 'BMS', 'Capital Advisory', 'Internal', 1),
        (15, 'Chaitanaya', 'Vij', 'Associate', 'manya.vij@bmsgroup.com', NULL, 'BMS', 'Capital Advisory', 'Internal', 1),
        (16, 'Tenzing Rapden', 'Lama', 'Associate', 'rapden.lama@bmsgroup.com', NULL, 'BMS', 'Capital Advisory', 'Internal', 1),
        (17, 'Qudus', 'Kassim', 'Capital Advisory Associate', 'qudus.kassim@bmsgroup.com', NULL, 'BMS', 'Capital Advisory', 'Internal', 1),
        (18, 'Josh', 'Curry', 'Chief Growth Officer', 'josh.curry@bmsgroup.com', NULL, 'BMS', 'Capital Advisory', 'Internal', 1),
        (19, 'James', 'Bradley', 'Director', 'james.bradley@bmsgroup.com', NULL, 'BMS', 'Capital Advisory', 'Internal', 1),
        (20, 'Michael', 'O''Connell', 'Director', 'michael.oconnell@bmsgroup.com', NULL, 'BMS', 'Capital Advisory', 'Internal', 1),
        (21, 'Nicholas', 'Carey', 'Associate Broker', 'nick.carey@bmsgroup.com', '404-270-0362', 'BMS', 'P&C Treaty', 'Internal', 1),
        (22, 'Scott', 'Fields', 'Senior Vice President', 'scott.fields@bmsgroup.com', '404-353-7420', 'BMS', 'P&C Treaty', 'Internal', 1),
        (23, 'Christopher', 'McCrary', 'Managing Director, Branch Manager', 'sean.mccrary@bmsgroup.com', '214-538-8040', 'BMS', 'P&C Treaty', 'Internal', 1),
        (24, 'Thomas', 'Evans', 'Vice President', 'thomas.evans@bmsgroup.com', '682-712-4934', 'BMS', 'P&C Treaty', 'Internal', 1),
        (25, 'Cody', 'Sigman', 'Vice President', 'cody.sigman@bmsgroup.com', '945-336-6156', 'BMS', 'P&C Treaty', 'Internal', 1),
        (26, 'Levi', 'Nelson', 'Broker', 'levi.nelson@bmsgroup.com', '612-417-9883', 'BMS', 'P&C Treaty', 'Internal', 1),
        (27, 'David', 'Cameron', 'Executive Vice President', 'david.cameron@bmsgroup.com', '612-443-2199', 'BMS', 'P&C Treaty', 'Internal', 1),
        (28, 'Nathan', 'Pelant', 'Broker', 'nathan.pelant@bmsgroup.com', '612-443-7245', 'BMS', 'P&C Treaty', 'Internal', 1),
        (29, 'Steven', 'Galleger', 'Managing Director, Branch Manager', 'Steve.Galleger@bmsgroup.com', '952-258-3936', 'BMS', 'P&C Treaty', 'Internal', 1),
        (30, 'Matthew', 'Blanch', 'Senior Vice President', 'matt.blanch@bmsgroup.com', '952-388-8507', 'BMS', 'P&C Treaty', 'Internal', 1),
        (31, 'Alexander', 'Kracht', 'Senior Vice President', 'alex.kracht@bmsgroup.com', '312-208-8243', 'BMS', 'P&C Treaty', 'Internal', 1),
        (32, 'Matthew', 'Cratem', 'Vice President', 'matthew.cratem@bmsgroup.com', '904-742-5241', 'BMS', 'P&C Treaty', 'Internal', 1),
        (33, 'Jack', 'Idelson', 'Broker', 'jack.idelson@bmsgroup.com', NULL, 'BMS', 'P&C Treaty', 'Internal', 1),
        (34, 'John', 'Hickey', 'Managing Director, Branch Manager', 'jack.hickey@bmsgroup.com', '203-308-6333', 'BMS', 'P&C Treaty', 'Internal', 1),
        (35, 'Colin', 'Curran', 'Assistant Vice President', 'colin.curran@bmsgroup.com', '347-410-4513', 'BMS', 'P&C Treaty', 'Internal', 1),
        (36, 'Philip', 'Clementi', 'Broker', 'pj.clementi@bmsgroup.com', '347-420-4100', 'BMS', 'P&C Treaty', 'Internal', 1),
        (37, 'Carter', 'McFadden', 'Associate Broker', 'carter.mcfadden@bmsgroup.com', '347-429-4435', 'BMS', 'P&C Treaty', 'Internal', 1),
        (38, 'Hunter', 'Rotchford', 'Senior Vice President', 'hunter.rotchford@bmsgroup.com', '347-754-0552', 'BMS', 'P&C Treaty', 'Internal', 1),
        (39, 'Gavin', 'Brennan', 'Senior Vice President', 'gavin.brennan@bmsgroup.com', '516-528-3661', 'BMS', 'P&C Treaty', 'Internal', 1),
        (40, 'Jordi', 'Jefferson', 'Assistant Vice President', 'jordi.jefferson@bmsgroup.com', '646-629-3087', 'BMS', 'P&C Treaty', 'Internal', 1),
        (41, 'Charles', 'Ill', 'Vice President', 'charles.ill@bmsgroup.com', '646-830-4561', 'BMS', 'P&C Treaty', 'Internal', 1),
        (42, 'Frank', 'Belarge', 'Broker', 'frank.jbelarge@bmsgroup.com', '718-406-3084', 'BMS', 'P&C Treaty', 'Internal', 1),
        (43, 'Anthony', 'Blanton', 'Vice President', 'clarke.blanton@bmsgroup.com', '917-587-0479', 'BMS', 'P&C Treaty', 'Internal', 1),
        (44, 'Michael', 'Kearney', 'Broker', 'michael.kearney@bmsgroup.com', '917-854-1315', 'BMS', 'P&C Treaty', 'Internal', 1),
        (45, 'Reagan', 'Phillips', 'Broker', 'reagan.phillips@bmsgroup.com', '929-286-2044', 'BMS', 'P&C Treaty', 'Internal', 1),
        (46, 'Chase', 'Moresco', 'Vice President', 'chase.moresco@bmsgroup.com', '445-247-5369', 'BMS', 'P&C Treaty', 'Internal', 1),
        (47, 'Jake', 'LaCava', 'Assistant Vice President', 'jake.lacava@bmsgroup.com', '484-981-5267', 'BMS', 'P&C Treaty', 'Internal', 1),
        (48, 'Thomas', 'Spataro', 'Managing Director', 'thomas.spatarosr@bmsgroup.com', NULL, 'BMS', 'P&C Treaty', 'Internal', 1),
        (49, 'Edward', 'Ryan', 'Senior Vice President', 'edward.ryan@bmsgroup.com', '904-921-6504', 'BMS', 'P&C Treaty', 'Internal', 1),
        (50, 'Nathaniel', 'Robison', 'Broker', 'nate.robison@bmsgroup.com', '215-796-7005', 'BMS', 'P&C Treaty', 'Internal', 1),
        (51, 'Tom', 'Spataro', 'Vice President', 'tom.spatarojr@bmsgroup.com', NULL, 'BMS', 'P&C Treaty', 'Internal', 1),
        (52, 'Christa', 'Coletti', 'Managing Director, Branch Manager', 'christa.coletti@bmsgroup.com', '215-688-1417', 'BMS', 'P&C Treaty', 'Internal', 1),
        (53, 'Kristin', 'Callahan', 'Senior Vice President', 'kristin.callahan@bmsgroup.com', '267-253-1550', 'BMS', 'P&C Treaty', 'Internal', 1),
        (54, 'Julie', 'Weiss', 'Assistant Vice President', 'julie.weiss@bmsgroup.com', '267-582-4788', 'BMS', 'P&C Treaty', 'Internal', 1),
        (55, 'Denise', 'Fratanduono', 'Vice President', 'denise.fratanduono@bmsgroup.com', '267-738-1162', 'BMS', 'P&C Treaty', 'Internal', 1),
        (56, 'John', 'Faustman', 'Assistant Vice President', 'john.faustmanjr@bmsgroup.com', '267-825-0815', 'BMS', 'P&C Treaty', 'Internal', 1),
        (57, 'Matthew', 'O''Connor', 'Associate Broker', 'matt.oconnor@bmsgroup.com', '367-251-4917', 'BMS', 'P&C Treaty', 'Internal', 1),
        (58, 'David', 'Galleger', 'Executive Vice President', 'dave.galleger@bmsgroup.com', '469-933-4331', 'BMS', 'P&C Treaty', 'Internal', 1),
        (59, 'Christopher', 'Ervey', 'Executive Vice President', 'chris.ervey@bmsgroup.com', '562-393-3628', 'BMS', 'P&C Treaty', 'Internal', 1),
        (60, 'Nathan', 'Henke', 'Assistant Vice President', 'nathan.henke@bmsgroup.com', '612-946-1964', 'BMS', 'P&C Treaty', 'Internal', 1),
        (61, 'Mark', 'LaVoie', 'Senior Vice President', 'mark.lavoie@bmsgroup.com', '320-342-8071', 'BMS', 'P&C Treaty', 'Internal', 1),
        (62, 'Jaclyn', 'Clemson', 'Executive Vice President', 'micki.clemson@bmsgroup.com', '612-804-6609', 'BMS', 'P&C Treaty', 'Internal', 1),
        (63, 'Bennett', 'Soderberg', 'Associate Broker', 'bennett.soderberg@bmsgroup.com', '952-233-1340', 'BMS', 'P&C Treaty', 'Internal', 1),
        (64, 'William', 'Penn', 'Executive Vice President', 'william.penn@bmsgroup.com', '630-796-5362', 'BMS', 'Prolink', 'Internal', 1),
        (65, 'John', 'Speckman', 'Head of Programs', 'john.speckman@bmsgroup.com', '917-696-8315', 'BMS', 'Prolink', 'Internal', 1),
        (66, 'Brenda', 'Martinez-Cox', 'Technical Support Associate', 'brenda.martinez-cox@bmsgroup.com', '929-618-9414', 'BMS', 'Prolink', 'Internal', 1),
        (67, 'Shruti', 'Sem', 'Managing Director', 'shruti.sem@bmsgroup.com', '267-374-4438', 'BMS', 'Prolink', 'Internal', 1),
        (68, 'Andrew', 'Justice', 'Managing Director', 'andy.justice@bmsgroup.com', '214-536-0089', 'BMS', 'P&C Treaty', 'Internal', 1),
        (69, 'Peter', 'Magliaro', 'EVP, Head of Actuarial Services', 'Peter.Magliaro@bmsgroup.com', NULL, 'BMS', 'Actuarial Services', 'Internal', 1),
        (70, 'Simon', 'Chowaniec', 'Senior Vice President Actuary', 'Simon.Chowaniec@bmsgroup.com', '972-322-8418', 'BMS', 'Actuarial Services', 'Internal', 1),
        (71, 'Greg', 'Spindell', 'Executive Vice President', 'Greg.Spindell@bmsgroup.com', NULL, 'BMS', 'Actuarial Services', 'Internal', 1),
        (72, 'Clare', 'Whitlam', 'Executive Vice President', 'Clare.Whitlam@bmsgroup.com', '415-734-8931', 'BMS', 'Actuarial Services', 'Internal', 1),
        (73, 'Kurt', 'Johnson', 'Chief Actuary', 'Kurt.Johnson@bmsgroup.com', '612-993642', 'BMS', 'Actuarial Services', 'Internal', 1),
        (74, 'Joe', 'Tripodi', 'Senior Vice President Actuary', 'Joe.Tripodi@bmsgroup.com', '609-647-2400', 'BMS', 'Actuarial Services', 'Internal', 1),
        (75, 'Meghan', 'Aydin', 'Vice President Actuary', 'Meghan.Aydin@bmsgroup.com', '718-687-3746', 'BMS', 'Actuarial Services', 'Internal', 1),
        (76, 'Chase', 'Benoist', 'Vice President', 'Chase.Benoist@bmsgroup.com', '470-494-9596', 'BMS', 'Actuarial Services', 'Internal', 1),
        (77, 'TJ', 'Rogers', 'Vice President', 'TJ.Rogers@bmsgroup.com', '612-930-6337', 'BMS', 'Actuarial Services', 'Internal', 1),
        (78, 'Daniel', 'Tully', 'Actuary', 'Daniel.Tully@bmsgroup.com', '44-20-7480-4389', 'BMS', 'Actuarial Services', 'Internal', 1),
        (79, 'Anna', 'Kazachka', 'Actuarial Senior Analyst', 'Anna.Kazachka@bmsgroup.com', '469-607184', 'BMS', 'Actuarial Services', 'Internal', 1),
        (80, 'Wei', 'Sheng', 'Actuarial Senior Analyst', 'Wei.Sheng@bmsgroup.com', '612-479-5968', 'BMS', 'Actuarial Services', 'Internal', 1),
        (81, 'Genieva', 'Beckstrand', 'Actuarial Senior Analyst', 'Genieva.Beckstrand@bmsgroup.com', '952-463-3856', 'BMS', 'Actuarial Services', 'Internal', 1),
        (82, 'Sy', 'Neeley', 'Senior Actuarial Analyst', 'Sy.Neeley@bmsgroup.com', '469-560-3305', 'BMS', 'Actuarial Services', 'Internal', 1),
        (83, 'Tony', 'Haack', 'Intern', 'Tony.Haack@bmsgroup.com', NULL, 'BMS', 'Actuarial Services', 'Internal', 1)
) AS src(UserID, FirstName, LastName, Title, Email, Phone, Company, Department, UserType, StatusID)
ON tgt.UserID = src.UserID
WHEN MATCHED THEN
    UPDATE SET
        FirstName = src.FirstName,
        LastName = src.LastName,
        Title = src.Title,
        Email = src.Email,
        Phone = src.Phone,
        Company = src.Company,
        Department = src.Department,
        UserType = src.UserType,
        StatusID = src.StatusID,
        IsDeleted = 0,
        DeletedDate = NULL,
        ModifiedDate = SYSUTCDATETIME()
WHEN NOT MATCHED THEN
    INSERT (UserID, FirstName, LastName, Title, Email, Phone, Company, Department, UserType, StatusID, IsDeleted)
    VALUES (src.UserID, src.FirstName, src.LastName, src.Title, src.Email, src.Phone, src.Company, src.Department, src.UserType, src.StatusID, 0);

SET IDENTITY_INSERT dbo.tUser OFF;
GO

DECLARE @MaxUserID INT = (SELECT ISNULL(MAX(UserID), 0) FROM dbo.tUser);
DBCC CHECKIDENT ('dbo.tUser', RESEED, @MaxUserID);
GO

MERGE dbo.tUserAddress AS tgt
USING (VALUES
        (1, 'Work', '8000 Avalon Blvd., Suite 810', NULL, 'Alpharetta', 'GA', '30009', 'US'),
        (2, 'Work', '7650 Edinborough Way, STE 600', NULL, 'Edina', 'MN', NULL, 'US'),
        (3, 'Work', '7650 Edinborough Way, STE 600', NULL, 'Edina', 'MN', NULL, 'US'),
        (4, 'Work', '7650 Edinborough Way, STE 600', NULL, 'Edina', 'MN', NULL, 'US'),
        (5, 'Work', '8000 Avalon Blvd., Suite 810', NULL, 'Alpharetta', 'GA', NULL, 'US'),
        (6, 'Work', '7650 Edinborough Way, STE 600', NULL, 'Edina', 'MN', NULL, 'US'),
        (7, 'Work', '8000 Avalon Blvd., Suite 810', NULL, 'Alpharetta', 'GA', NULL, 'US'),
        (8, 'Work', '8000 Avalon Boulevard, Suite 810', NULL, 'Alpharetta', 'GA', NULL, 'US'),
        (9, 'Work', '757 3rd Ave, STE 1600', NULL, 'New York', 'NY', NULL, 'US'),
        (10, 'Work', 'N/A', NULL, 'Mumbai', NULL, NULL, 'India'),
        (11, 'Work', 'N/A', NULL, 'Mumbai', NULL, NULL, 'India'),
        (12, 'Work', 'N/A', NULL, 'Mumbai', NULL, NULL, 'India'),
        (13, 'Work', 'BMS Re 757 3rd Ave, STE 1602', NULL, 'New York', 'NY', NULL, 'US'),
        (14, 'Work', 'BMS Re 757 3rd Ave, STE 1603', NULL, 'New York', 'NY', NULL, 'US'),
        (15, 'Work', 'BMS Re 757 3rd Ave, STE 1604', NULL, 'New York', 'NY', NULL, 'US'),
        (16, 'Work', 'BMS Re 757 3rd Ave, STE 1605', NULL, 'New York', 'NY', NULL, 'US'),
        (17, 'Work', 'BMS Re 757 3rd Ave, STE 1606', NULL, 'New York', 'NY', NULL, 'US'),
        (18, 'Work', 'BMS Re 757 3rd Ave, STE 1607', NULL, 'New York', 'NY', NULL, 'US'),
        (19, 'Work', 'BMS Re 757 3rd Ave, STE 1608', NULL, 'New York', 'NY', NULL, 'US'),
        (20, 'Work', 'BMS Re 757 3rd Ave, STE 1609', NULL, 'New York', 'NY', NULL, 'US'),
        (21, 'Work', 'BMS Re 8000 Avalon Boulevard Suite 816', NULL, 'Atlanta', 'GA', NULL, 'US'),
        (22, 'Work', 'BMS Re 8000 Avalon Boulevard Suite 817', NULL, 'Atlanta', 'GA', NULL, 'US'),
        (23, 'Work', 'BMS Re 2651 North Harwood Street, Suite 428', NULL, 'Dallas', 'TX', NULL, 'US'),
        (24, 'Work', 'BMS Re 2651 North Harwood Street, Suite 429', NULL, 'Dallas', 'TX', NULL, 'US'),
        (25, 'Work', 'BMS Re 2651 North Harwood Street, Suite 430', NULL, 'Dallas', 'TX', NULL, 'US'),
        (26, 'Work', 'BMS Re 7650 Edinborough Way, Suite 603', NULL, 'Edina', 'MN', NULL, 'US'),
        (27, 'Work', 'BMS Re 7650 Edinborough Way, Suite 604', NULL, 'Edina', 'MN', NULL, 'US'),
        (28, 'Work', 'BMS Re 7650 Edinborough Way, Suite 605', NULL, 'Edina', 'MN', NULL, 'US'),
        (29, 'Work', 'BMS Re 7650 Edinborough Way, Suite 606', NULL, 'Edina', 'MN', NULL, 'US'),
        (30, 'Work', 'BMS Re 7650 Edinborough Way, Suite 607', NULL, 'Edina', 'MN', NULL, 'US'),
        (31, 'Work', 'WeWork c/o BMS Re, 515 N. State Street, 14th Floor', NULL, 'Chicago', 'IL', NULL, 'US'),
        (32, 'Work', 'BMS Re 757 3rd Ave, STE 1601', NULL, 'New York', 'NY', NULL, 'US'),
        (33, 'Work', 'BMS Re 757 3rd Ave, STE 1611', NULL, 'New York', 'NY', NULL, 'US'),
        (34, 'Work', 'BMS Re 757 3rd Ave, STE 1612', NULL, 'New York', 'NY', NULL, 'US'),
        (35, 'Work', 'BMS Re 757 3rd Ave, STE 1613', NULL, 'New York', 'NY', NULL, 'US'),
        (36, 'Work', 'BMS Re 757 3rd Ave, STE 1614', NULL, 'New York', 'NY', NULL, 'US'),
        (37, 'Work', 'BMS Re 757 3rd Ave, STE 1615', NULL, 'New York', 'NY', NULL, 'US'),
        (38, 'Work', 'BMS Re 757 3rd Ave, STE 1616', NULL, 'New York', 'NY', NULL, 'US'),
        (39, 'Work', 'BMS Re 757 3rd Ave, STE 1617', NULL, 'New York', 'NY', NULL, 'US'),
        (40, 'Work', 'BMS Re 757 3rd Ave, STE 1618', NULL, 'New York', 'NY', NULL, 'US'),
        (41, 'Work', 'BMS Re 757 3rd Ave, STE 1619', NULL, 'New York', 'NY', NULL, 'US'),
        (42, 'Work', 'BMS Re 757 3rd Ave, STE 1620', NULL, 'New York', 'NY', NULL, 'US'),
        (43, 'Work', 'BMS Re 757 3rd Ave, STE 1621', NULL, 'New York', 'NY', NULL, 'US'),
        (44, 'Work', 'BMS Re 757 3rd Ave, STE 1622', NULL, 'New York', 'NY', NULL, 'US'),
        (45, 'Work', 'BMS Re 757 3rd Ave, STE 1623', NULL, 'New York', 'NY', NULL, 'US'),
        (46, 'Work', 'BMS Re 1818 Market Street, Suite 1158', NULL, 'Philadelphia', 'PA', NULL, 'US'),
        (47, 'Work', 'BMS Re 1818 Market Street, Suite 1159', NULL, 'Philadelphia', 'PA', NULL, 'US'),
        (48, 'Work', 'BMS Re 1818 Market Street, Suite 1152', NULL, 'Philadelphia', 'PA', NULL, 'US'),
        (49, 'Work', 'BMS Re 1818 Market Street, Suite 1153', NULL, 'Philadelphia', 'PA', NULL, 'US'),
        (50, 'Work', 'BMS Re 1818 Market Street, Suite 1155', NULL, 'Philadelphia', 'PA', NULL, 'US'),
        (51, 'Work', 'BMS Re 1818 Market Street, Suite 1160', NULL, 'Philadelphia', 'PA', NULL, 'US'),
        (52, 'Work', 'BMS Re 1818 Market Street, Suite 1161', NULL, 'Philadelphia', 'PA', NULL, 'US'),
        (53, 'Work', 'BMS Re 1818 Market Street, Suite 1162', NULL, 'Philadelphia', 'PA', NULL, 'US'),
        (54, 'Work', 'BMS Re 1818 Market Street, Suite 1163', NULL, 'Philadelphia', 'PA', NULL, 'US'),
        (55, 'Work', 'BMS Re 1818 Market Street, Suite 1164', NULL, 'Philadelphia', 'PA', NULL, 'US'),
        (56, 'Work', 'BMS Re 1818 Market Street, Suite 1165', NULL, 'Philadelphia', 'PA', NULL, 'US'),
        (57, 'Work', 'BMS Re 1818 Market Street, Suite 1166', NULL, 'Philadelphia', 'PA', NULL, 'US'),
        (58, 'Work', 'BMS Re 2651 North Harwood Street, Suite 431', NULL, 'Dallas', 'TX', NULL, 'US'),
        (59, 'Work', 'BMS Re 7650 Edinborough Way, Suite 600', NULL, 'Edina', 'MN', NULL, 'US'),
        (60, 'Work', 'BMS Re 7650 Edinborough Way, Suite 601', NULL, 'Edina', 'MN', NULL, 'US'),
        (61, 'Work', 'BMS Re 7650 Edinborough Way, Suite 608', NULL, 'Edina', 'MN', NULL, 'US'),
        (62, 'Work', 'BMS Re 7650 Edinborough Way, Suite 609', NULL, 'Edina', 'MN', NULL, 'US'),
        (63, 'Work', 'BMS Re 7650 Edinborough Way, Suite 610', NULL, 'Edina', 'MN', NULL, 'US'),
        (64, 'Work', 'WeWork c/o BMS Re, 515 N. State Street, 14th Floor', NULL, 'Chicago', 'IL', NULL, 'US'),
        (65, 'Work', 'BMS Re 757 3rd Ave, STE 1624', NULL, 'New York', 'NY', NULL, 'US'),
        (66, 'Work', 'BMS Re 757 3rd Ave, STE 1625', NULL, 'New York', 'NY', NULL, 'US'),
        (67, 'Work', 'BMS Re 1818 Market Street, Suite 1167', NULL, 'Philadelphia', 'PA', NULL, 'US'),
        (68, 'Work', 'BMS Re 2651 North Harwood Street, Suite 433', NULL, 'Dallas', 'TX', NULL, 'US'),
        (69, 'Work', '757 3rd Ave, STE 1600', NULL, 'New York', 'NY', NULL, 'US'),
        (70, 'Work', '2651 North Harwood St.', NULL, 'Dallas', 'TX', NULL, 'US'),
        (71, 'Work', '757 3rd Ave, STE 1600', NULL, 'New York', 'NY', NULL, 'US'),
        (72, 'Work', 'N/A', NULL, 'San Francisco', 'CA', NULL, 'US'),
        (73, 'Work', 'N/A', NULL, 'Edina', 'MN', NULL, 'US'),
        (74, 'Work', 'N/A', NULL, 'Philadelphia', 'PA', NULL, 'US'),
        (75, 'Work', '757 3rd Ave, STE 1600', NULL, 'New York', 'NY', NULL, 'US'),
        (76, 'Work', '8000 Avalon Blvd Suite 810', NULL, 'Alpharetta', 'GA', NULL, 'US'),
        (77, 'Work', 'N/A', NULL, 'Edina', 'MN', NULL, 'US'),
        (78, 'Work', 'N/A', NULL, 'London', NULL, NULL, 'US'),
        (79, 'Work', '2651 North Harwood St.', NULL, 'Dallas', 'TX', NULL, 'US'),
        (80, 'Work', 'N/A', NULL, 'Edina', 'MN', NULL, 'US'),
        (81, 'Work', 'N/A', NULL, 'Edina', 'MN', NULL, 'US'),
        (82, 'Work', '2651 North Harwood St.', NULL, 'Dallas', 'TX', NULL, 'US'),
        (83, 'Work', 'Edina Office', NULL, 'Edina', 'MN', NULL, 'US')
) AS src(UserID, AddressType, Address1, Address2, City, State, PostalCode, Country)
ON tgt.UserID = src.UserID
   AND tgt.AddressType = src.AddressType
WHEN MATCHED THEN
    UPDATE SET
        Address1 = src.Address1,
        Address2 = src.Address2,
        City = src.City,
        State = src.State,
        PostalCode = src.PostalCode,
        Country = src.Country,
        ModifiedDate = SYSUTCDATETIME()
WHEN NOT MATCHED THEN
    INSERT (UserID, AddressType, Address1, Address2, City, State, PostalCode, Country)
    VALUES (src.UserID, src.AddressType, src.Address1, src.Address2, src.City, src.State, src.PostalCode, src.Country);
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/* ============================================================
   Cross-cutting helpers
   ============================================================ */

CREATE OR ALTER PROCEDURE dbo.usp_ActivityLog_Insert
    @EntityType VARCHAR(50),
    @EntityID INT,
    @Action VARCHAR(30),
    @FieldName VARCHAR(100) = NULL,
    @OldValue NVARCHAR(MAX) = NULL,
    @NewValue NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    INSERT dbo.tActivityLog(EntityType, EntityID, Action, FieldName, OldValue, NewValue)
    VALUES (@EntityType, @EntityID, @Action, @FieldName, @OldValue, @NewValue);
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Status_ValidateForEntity
    @StatusID INT,
    @EntityType VARCHAR(30),
    @IsValid BIT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT @IsValid =
        CASE WHEN EXISTS
        (
            SELECT 1
            FROM dbo.tStatus s
            JOIN dbo.tStatusAppliesTo sat ON sat.StatusID = s.StatusID
            WHERE s.StatusID = @StatusID
              AND s.IsActive = 1
              AND (sat.EntityType = @EntityType OR sat.EntityType = 'Universal')
        )
        THEN 1 ELSE 0 END;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Status_GetByCode
    @StatusCode VARCHAR(20),
    @StatusTypeID INT,
    @StatusID INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT @StatusID = s.StatusID
    FROM dbo.tStatus s
    WHERE s.StatusCode = @StatusCode
      AND s.StatusTypeID = @StatusTypeID
      AND s.IsActive = 1;

    IF @StatusID IS NULL
        THROW 50001, 'Status code not found for supplied StatusTypeID.', 1;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Status_ListByEntity
    @EntityType VARCHAR(30)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT DISTINCT s.StatusID, s.StatusCode, s.StatusDescription, st.TypeName AS StatusType
    FROM dbo.tStatus s
    JOIN dbo.tStatusType st ON s.StatusTypeID = st.StatusTypeID
    JOIN dbo.tStatusAppliesTo sat ON sat.StatusID = s.StatusID
    WHERE s.IsActive = 1
      AND (sat.EntityType = @EntityType OR sat.EntityType = 'Universal')
    ORDER BY s.StatusCode;
END;
GO

/* ============================================================
   Lookup GetOrCreate + list procedures
   ============================================================ */
--Business Unit
CREATE OR ALTER PROCEDURE dbo.usp_BusinessUnit_GetOrCreate
    @BusinessUnitName VARCHAR(100),
    @BusinessUnitDescription VARCHAR(255) = NULL,
    @BusinessUnitID INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NULLIF(LTRIM(RTRIM(@BusinessUnitName)), '') IS NULL
        THROW 50002, 'BusinessUnitName is required.', 1;

    SELECT @BusinessUnitID = BusinessUnitID
    FROM dbo.tBusinessUnit WITH (UPDLOCK, HOLDLOCK)
    WHERE BusinessUnitName = @BusinessUnitName;

    IF @BusinessUnitID IS NULL
    BEGIN
        INSERT dbo.tBusinessUnit(BusinessUnitName, BusinessUnitDescription)
        VALUES (@BusinessUnitName, @BusinessUnitDescription);
        SET @BusinessUnitID = SCOPE_IDENTITY();

        EXEC dbo.usp_ActivityLog_Insert 'BusinessUnit', @BusinessUnitID, 'Insert', NULL, NULL, @BusinessUnitName;
    END
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_BusinessUnit_List
    @ActiveOnly BIT = 0
AS
BEGIN
    SET NOCOUNT ON;
    SELECT BusinessUnitID, BusinessUnitName, BusinessUnitDescription, IsActive, CreatedDate, ModifiedDate
    FROM dbo.tBusinessUnit
    WHERE @ActiveOnly = 0 OR IsActive = 1
    ORDER BY BusinessUnitName;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_BusinessUnit_Update
    @BusinessUnitID INT,
    @BusinessUnitName VARCHAR(100) = NULL,
    @BusinessUnitDescription VARCHAR(255) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.tBusinessUnit WHERE BusinessUnitID = @BusinessUnitID)
        THROW 50079, 'BusinessUnit not found.', 1;

    IF @BusinessUnitName IS NOT NULL AND EXISTS
    (
        SELECT 1
        FROM dbo.tBusinessUnit
        WHERE BusinessUnitName = @BusinessUnitName
          AND BusinessUnitID <> @BusinessUnitID
    )
        THROW 50080, 'BusinessUnit name already exists.', 1;

    UPDATE dbo.tBusinessUnit
    SET BusinessUnitName = COALESCE(@BusinessUnitName, BusinessUnitName),
        BusinessUnitDescription = COALESCE(@BusinessUnitDescription, BusinessUnitDescription),
        ModifiedDate = SYSUTCDATETIME()
    WHERE BusinessUnitID = @BusinessUnitID;

    EXEC dbo.usp_ActivityLog_Insert 'BusinessUnit', @BusinessUnitID, 'Update', NULL, NULL, NULL;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_BusinessUnit_SetActive
    @BusinessUnitID INT,
    @IsActive BIT
AS
BEGIN
    SET NOCOUNT ON;

    IF @IsActive = 0 AND EXISTS (SELECT 1 FROM dbo.tBusinessClass WHERE BusinessUnitID = @BusinessUnitID)
        THROW 50003, 'Cannot deactivate: BusinessClasses reference this BusinessUnit.', 1;

    UPDATE dbo.tBusinessUnit
    SET IsActive = @IsActive, ModifiedDate = SYSUTCDATETIME()
    WHERE BusinessUnitID = @BusinessUnitID;

    IF @@ROWCOUNT = 0 THROW 50004, 'BusinessUnit not found.', 1;
    EXEC dbo.usp_ActivityLog_Insert 'BusinessUnit', @BusinessUnitID, 'SetActive', 'IsActive', NULL, @IsActive;
END;
GO

--Business Class
CREATE OR ALTER PROCEDURE dbo.usp_BusinessClass_GetOrCreate
    @BusinessClassName VARCHAR(100),
    @BusinessClassDescription VARCHAR(255) = NULL,
    @BusinessUnitID INT,
    @BusinessClassID INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    IF NULLIF(LTRIM(RTRIM(@BusinessClassName)), '') IS NULL
        THROW 50005, 'BusinessClassName is required.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.tBusinessUnit WHERE BusinessUnitID = @BusinessUnitID AND IsActive = 1)
        THROW 50006, 'BusinessUnit not found or inactive.', 1;

    SELECT @BusinessClassID = BusinessClassID
    FROM dbo.tBusinessClass WITH (UPDLOCK, HOLDLOCK)
    WHERE BusinessUnitID = @BusinessUnitID AND BusinessClassName = @BusinessClassName;

    IF @BusinessClassID IS NULL
    BEGIN
        INSERT dbo.tBusinessClass(BusinessClassName, BusinessClassDescription, BusinessUnitID)
        VALUES (@BusinessClassName, @BusinessClassDescription, @BusinessUnitID);
        SET @BusinessClassID = SCOPE_IDENTITY();

        EXEC dbo.usp_ActivityLog_Insert 'BusinessClass', @BusinessClassID, 'Insert', NULL, NULL, @BusinessClassName;
    END
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_BusinessClass_List
    @BusinessUnitID INT = NULL,
    @IncludeInactive BIT = 0
AS
BEGIN
    SET NOCOUNT ON;
    SELECT bc.BusinessClassID, bc.BusinessClassName, bc.BusinessClassDescription, bc.BusinessUnitID, bu.BusinessUnitName, bu.IsActive AS BusinessUnitIsActive
    FROM dbo.tBusinessClass bc
    JOIN dbo.tBusinessUnit bu ON bc.BusinessUnitID = bu.BusinessUnitID
    WHERE (@BusinessUnitID IS NULL OR bc.BusinessUnitID = @BusinessUnitID)
      AND (@IncludeInactive = 1 OR bu.IsActive = 1)
    ORDER BY bu.BusinessUnitName, bc.BusinessClassName;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_BusinessClass_Update
    @BusinessClassID INT,
    @BusinessClassName VARCHAR(100) = NULL,
    @BusinessClassDescription VARCHAR(255) = NULL,
    @BusinessUnitID INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @OldBusinessUnitID INT;
    SELECT @OldBusinessUnitID = BusinessUnitID FROM dbo.tBusinessClass WHERE BusinessClassID = @BusinessClassID;
    IF @OldBusinessUnitID IS NULL THROW 50007, 'BusinessClass not found.', 1;

    IF @BusinessUnitID IS NOT NULL AND @BusinessUnitID <> @OldBusinessUnitID
       AND EXISTS (SELECT 1 FROM dbo.tClient WHERE BusinessClassID = @BusinessClassID AND IsDeleted = 0)
        THROW 50008, 'Cannot change BusinessUnitID: active clients reference this BusinessClass.', 1;

    UPDATE dbo.tBusinessClass
    SET BusinessClassName = COALESCE(@BusinessClassName, BusinessClassName),
        BusinessClassDescription = COALESCE(@BusinessClassDescription, BusinessClassDescription),
        BusinessUnitID = COALESCE(@BusinessUnitID, BusinessUnitID),
        ModifiedDate = SYSUTCDATETIME()
    WHERE BusinessClassID = @BusinessClassID;

    EXEC dbo.usp_ActivityLog_Insert 'BusinessClass', @BusinessClassID, 'Update', NULL, NULL, NULL;
END;
GO

--ProjectType
CREATE OR ALTER PROCEDURE dbo.usp_ProjectType_GetOrCreate
    @PTCode VARCHAR(20),
    @PTDescription VARCHAR(255) = NULL,
    @ProjectTypeID INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    IF NULLIF(LTRIM(RTRIM(@PTCode)), '') IS NULL THROW 50011, 'PTCode is required.', 1;

    SELECT @ProjectTypeID = ProjectTypeID
    FROM dbo.tProjectType WITH (UPDLOCK, HOLDLOCK)
    WHERE PTCode = @PTCode;

    IF @ProjectTypeID IS NULL
    BEGIN
        INSERT dbo.tProjectType(PTCode, PTDescription) VALUES (@PTCode, @PTDescription);
        SET @ProjectTypeID = SCOPE_IDENTITY();
        EXEC dbo.usp_ActivityLog_Insert 'ProjectType', @ProjectTypeID, 'Insert', NULL, NULL, @PTCode;
    END
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_ProjectType_List
    @IncludeInactive BIT = 0
AS
BEGIN
    SET NOCOUNT ON;
    SELECT ProjectTypeID, PTCode, PTDescription, IsActive
    FROM dbo.tProjectType
    WHERE @IncludeInactive = 1 OR IsActive = 1
    ORDER BY PTCode;
END;
GO

--Service Category
CREATE OR ALTER PROCEDURE dbo.usp_ServiceCategory_GetOrCreate
    @CategoryName VARCHAR(100),
    @CatFunction VARCHAR(100) = NULL,
    @ServiceCategoryID INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    IF NULLIF(LTRIM(RTRIM(@CategoryName)), '') IS NULL THROW 50012, 'CategoryName is required.', 1;

    SELECT @ServiceCategoryID = ServiceCategoryID
    FROM dbo.tServiceCategory WITH (UPDLOCK, HOLDLOCK)
    WHERE CategoryName = @CategoryName;

    IF @ServiceCategoryID IS NULL
    BEGIN
        INSERT dbo.tServiceCategory(CategoryName, CatFunction) VALUES (@CategoryName, @CatFunction);
        SET @ServiceCategoryID = SCOPE_IDENTITY();
        EXEC dbo.usp_ActivityLog_Insert 'ServiceCategory', @ServiceCategoryID, 'Insert', NULL, NULL, @CategoryName;
    END
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_ServiceCategory_List
    @ActiveOnly BIT = 0
AS
BEGIN
    SET NOCOUNT ON;
    SELECT ServiceCategoryID, CategoryName, CatFunction, IsActive, CreatedDate, ModifiedDate
    FROM dbo.tServiceCategory
    WHERE @ActiveOnly = 0 OR IsActive = 1
    ORDER BY CategoryName;
END;
GO

--User Role
CREATE OR ALTER PROCEDURE dbo.usp_UserRole_List
    @IncludeInactive BIT = 0
AS
BEGIN
    SET NOCOUNT ON;
    /* Source of truth: dbo.tUserRole. LEFT JOIN status so roles with NULL StatusID still appear in Admin. */
    SELECT
        r.UserRoleID,
        r.UserRoleName,
        r.RoleDescription,
        r.Tag1,
        r.Tag2,
        r.Tag3,
        r.StatusID,
        s.StatusCode,
        s.StatusDescription,
        r.IsActive,
        r.CreatedDate,
        r.ModifiedDate
    FROM dbo.tUserRole r
    LEFT JOIN dbo.tStatus s ON r.StatusID = s.StatusID
    WHERE @IncludeInactive = 1 OR r.IsActive = 1
    ORDER BY r.UserRoleName;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_UserRole_Add
    @UserRoleName VARCHAR(100),
    @RoleDescription VARCHAR(255) = NULL,
    @Tag1 VARCHAR(50) = NULL,
    @Tag2 VARCHAR(50) = NULL,
    @Tag3 VARCHAR(50) = NULL,
    @StatusID INT = NULL,
    @IsActive BIT = 1,
    @NewUserRoleID INT = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    IF NULLIF(LTRIM(RTRIM(@UserRoleName)), '') IS NULL THROW 50013, 'UserRoleName is required.', 1;
    IF EXISTS (SELECT 1 FROM dbo.tUserRole WHERE UserRoleName = @UserRoleName)
        THROW 50063, 'UserRoleName already exists.', 1;

    INSERT dbo.tUserRole(UserRoleName, RoleDescription, Tag1, Tag2, Tag3, StatusID, IsActive)
    VALUES (@UserRoleName, @RoleDescription, @Tag1, @Tag2, @Tag3, @StatusID, @IsActive);

    SET @NewUserRoleID = SCOPE_IDENTITY();
    EXEC dbo.usp_ActivityLog_Insert 'UserRole', @NewUserRoleID, 'Insert', NULL, NULL, @UserRoleName;

    SELECT @NewUserRoleID AS NewUserRoleID;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_UserRole_Update
    @UserRoleID INT,
    @UserRoleName VARCHAR(100) = NULL,
    @RoleDescription VARCHAR(255) = NULL,
    @Tag1 VARCHAR(50) = NULL,
    @Tag2 VARCHAR(50) = NULL,
    @Tag3 VARCHAR(50) = NULL,
    @StatusID INT = NULL,
    @IsActive BIT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM dbo.tUserRole WHERE UserRoleID = @UserRoleID)
        THROW 50064, 'User role not found.', 1;

    IF @UserRoleName IS NOT NULL
       AND EXISTS (SELECT 1 FROM dbo.tUserRole WHERE UserRoleName = @UserRoleName AND UserRoleID <> @UserRoleID)
        THROW 50065, 'UserRoleName already exists.', 1;

    UPDATE dbo.tUserRole
    SET UserRoleName = COALESCE(@UserRoleName, UserRoleName),
        RoleDescription = COALESCE(@RoleDescription, RoleDescription),
        Tag1 = COALESCE(@Tag1, Tag1),
        Tag2 = COALESCE(@Tag2, Tag2),
        Tag3 = COALESCE(@Tag3, Tag3),
        StatusID = COALESCE(@StatusID, StatusID),
        IsActive = COALESCE(@IsActive, IsActive),
        ModifiedDate = SYSUTCDATETIME()
    WHERE UserRoleID = @UserRoleID;

    EXEC dbo.usp_ActivityLog_Insert 'UserRole', @UserRoleID, 'Update', NULL, NULL, NULL;

    SELECT @UserRoleID AS UserRoleID;
END;
GO

--Tag
CREATE OR ALTER PROCEDURE dbo.usp_Tag_GetOrCreate
    @TagName VARCHAR(50),
    @TagScope VARCHAR(30),
    @TagID INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    IF NULLIF(LTRIM(RTRIM(@TagName)), '') IS NULL THROW 50014, 'TagName is required.', 1;
    IF NULLIF(LTRIM(RTRIM(@TagScope)), '') IS NULL THROW 50015, 'TagScope is required.', 1;

    SELECT @TagID = TagID
    FROM dbo.tTag WITH (UPDLOCK, HOLDLOCK)
    WHERE TagName = @TagName AND TagScope = @TagScope;

    IF @TagID IS NULL
    BEGIN
        INSERT dbo.tTag(TagName, TagScope) VALUES (@TagName, @TagScope);
        SET @TagID = SCOPE_IDENTITY();
        EXEC dbo.usp_ActivityLog_Insert 'Tag', @TagID, 'Insert', NULL, NULL, @TagName;
    END
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Tag_List
    @TagScope VARCHAR(30) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TagID, TagName, TagScope, CreatedDate
    FROM dbo.tTag
    WHERE @TagScope IS NULL OR TagScope = @TagScope
    ORDER BY TagScope, TagName;
END;
GO

/* ============================================================
   Client procedures
   ============================================================ */

CREATE OR ALTER PROCEDURE dbo.usp_Client_Add
    @BusinessUnitName VARCHAR(100),
    @BusinessClassName VARCHAR(100),
    @CCName VARCHAR(100),
    @CTName VARCHAR(100),
    @ClientName VARCHAR(150),
    @ClientShortName VARCHAR(50) = NULL,
    @StatusCode VARCHAR(20),
    @NewClientID INT = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @BusinessUnitID INT, @BusinessClassID INT, @StatusID INT;
    BEGIN TRY
        BEGIN TRAN;

        EXEC dbo.usp_BusinessUnit_GetOrCreate @BusinessUnitName, NULL, @BusinessUnitID OUTPUT;
        EXEC dbo.usp_BusinessClass_GetOrCreate @BusinessClassName, NULL, @BusinessUnitID, @BusinessClassID OUTPUT;

        SELECT TOP (1) @StatusID = s.StatusID
        FROM dbo.tStatus s
        JOIN dbo.tStatusType st ON s.StatusTypeID = st.StatusTypeID
        WHERE s.StatusCode = @StatusCode AND st.TypeName IN ('Client','Universal') AND s.IsActive = 1
        ORDER BY CASE WHEN st.TypeName = 'Client' THEN 0 ELSE 1 END;

        IF @StatusID IS NULL THROW 50016, 'Client status code not found.', 1;

        IF EXISTS (SELECT 1 FROM dbo.tClient WHERE ClientName = @ClientName AND IsDeleted = 0)
            THROW 50017, 'Client name already exists.', 1;

        INSERT dbo.tClient(ClientName, ClientShortName, BusinessClassID, StatusID)
        VALUES (@ClientName, @ClientShortName, @BusinessClassID, @StatusID);

        SET @NewClientID = SCOPE_IDENTITY();

        EXEC dbo.usp_ActivityLog_Insert 'Client', @NewClientID, 'Insert', NULL, NULL, @ClientName;

        COMMIT;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK;
        THROW;
    END CATCH

    SELECT @NewClientID AS NewClientID;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Client_Get
    @ClientID INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT *
    FROM dbo.vClient_WithBU
    WHERE ClientID = @ClientID;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Client_Update
    @ClientID INT,
    @ClientName VARCHAR(150) = NULL,
    @ClientShortName VARCHAR(50) = NULL,
    @BusinessClassID INT = NULL,
    @StatusID INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.tClient WHERE ClientID = @ClientID AND IsDeleted = 0)
        THROW 50018, 'Client not found or deleted.', 1;

    IF @ClientName IS NOT NULL AND EXISTS (SELECT 1 FROM dbo.tClient WHERE ClientName = @ClientName AND ClientID <> @ClientID AND IsDeleted = 0)
        THROW 50019, 'Client name already exists.', 1;

    IF @BusinessClassID IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.tBusinessClass WHERE BusinessClassID = @BusinessClassID)
        THROW 50020, 'BusinessClass not found.', 1;

    IF @StatusID IS NOT NULL
    BEGIN
        DECLARE @IsValid BIT;
        EXEC dbo.usp_Status_ValidateForEntity @StatusID, 'Client', @IsValid OUTPUT;
        IF @IsValid = 0 THROW 50021, 'Status not valid for Client entity.', 1;
    END

    UPDATE dbo.tClient
    SET ClientName = COALESCE(@ClientName, ClientName),
        ClientShortName = COALESCE(@ClientShortName, ClientShortName),
		MGA = COALESCE(@MGA, MGA),
		Program = COALESCE(@Program, Program),
        BusinessClassID = COALESCE(@BusinessClassID, BusinessClassID),
        StatusID = COALESCE(@StatusID, StatusID),
        ModifiedDate = SYSUTCDATETIME()
    WHERE ClientID = @ClientID;

    EXEC dbo.usp_ActivityLog_Insert 'Client', @ClientID, 'Update', NULL, NULL, NULL;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Client_List
    @BusinessUnitID INT = NULL,
    @StatusID INT = NULL,
    @SearchText VARCHAR(150) = NULL,
    @PageNumber INT = 1,
    @PageSize INT = 50
AS
BEGIN
    SET NOCOUNT ON;
    IF @PageNumber < 1 SET @PageNumber = 1;
    IF @PageSize < 1 SET @PageSize = 50;

    SELECT *
    FROM dbo.vClient_WithBU
    WHERE (@BusinessUnitID IS NULL OR BusinessUnitID = @BusinessUnitID)
      AND (@StatusID IS NULL OR StatusID = @StatusID)
      AND (@SearchText IS NULL OR ClientName LIKE '%' + @SearchText + '%' OR ClientShortName LIKE '%' + @SearchText + '%')
    ORDER BY ClientName
    OFFSET (@PageNumber - 1) * @PageSize ROWS FETCH NEXT @PageSize ROWS ONLY;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Client_SoftDelete
    @ClientID INT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.tClient
    SET IsDeleted = 1, DeletedDate = SYSUTCDATETIME(), ModifiedDate = SYSUTCDATETIME()
    WHERE ClientID = @ClientID AND IsDeleted = 0;

    IF @@ROWCOUNT = 0 THROW 50022, 'Client not found or already deleted.', 1;

    EXEC dbo.usp_ActivityLog_Insert 'Client', @ClientID, 'SoftDelete', NULL, NULL, NULL;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Client_Restore
    @ClientID INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @ClientName VARCHAR(150);
    SELECT @ClientName = ClientName FROM dbo.tClient WHERE ClientID = @ClientID AND IsDeleted = 1;

    IF @ClientName IS NULL THROW 50023, 'Deleted client not found.', 1;
    IF EXISTS (SELECT 1 FROM dbo.tClient WHERE ClientName = @ClientName AND IsDeleted = 0)
        THROW 50024, 'Cannot restore: active client with same ClientName exists.', 1;

    UPDATE dbo.tClient
    SET IsDeleted = 0, DeletedDate = NULL, ModifiedDate = SYSUTCDATETIME()
    WHERE ClientID = @ClientID;

    EXEC dbo.usp_ActivityLog_Insert 'Client', @ClientID, 'Restore', NULL, NULL, NULL;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Client_GetProjects
    @ClientID INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT *
    FROM dbo.vProject_Current
    WHERE ClientID = @ClientID
    ORDER BY DueDate DESC, ProjectNumber;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Client_GetSummary
    @ClientID INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        c.ClientID,
        c.ClientName,
        COUNT(DISTINCT p.ProjectID) AS ActiveProjectCount,
        SUM(COALESCE(vc.LatestRevenue, 0)) AS TotalLatestRevenue,
        COUNT(DISTINCT n.NoteID) AS OpenNoteCount,
        MAX(al.ActionDate) AS LastActivityDate
    FROM dbo.vClient c
    LEFT JOIN dbo.vProject_Current vc ON vc.ClientID = c.ClientID
    LEFT JOIN dbo.vProject p ON p.ProjectID = vc.ProjectID
    LEFT JOIN dbo.tProjectNote n ON n.ProjectID = p.ProjectID
    LEFT JOIN dbo.tActivityLog al ON al.EntityType = 'Client' AND al.EntityID = c.ClientID
    WHERE c.ClientID = @ClientID
    GROUP BY c.ClientID, c.ClientName;
END;
GO

/* ============================================================
   Project procedures
   ============================================================ */

CREATE OR ALTER PROCEDURE dbo.usp_Project_Add
    @ProjectNumber VARCHAR(50),
    @ProjectName VARCHAR(200),
    @ClientID INT,
    @UGID INT,
    @ProjectTypeID INT,
    @RenewalDate DATE = NULL,
    @DueDate DATE = NULL,
    @StatusID INT,
    @NewProjectID INT = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF EXISTS (SELECT 1 FROM dbo.tProject WHERE ProjectNumber = @ProjectNumber AND IsDeleted = 0)
        THROW 50025, 'Project number already exists.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.vClient WHERE ClientID = @ClientID)
        THROW 50026, 'Client not found or deleted.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.vUserGroup WHERE UGID = @UGID)
        THROW 50027, 'UserGroup not found or deleted.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.tProjectType WHERE ProjectTypeID = @ProjectTypeID AND IsActive = 1)
        THROW 50028, 'ProjectType not found or inactive.', 1;

    DECLARE @IsValid BIT;
    EXEC dbo.usp_Status_ValidateForEntity @StatusID, 'Project', @IsValid OUTPUT;
    IF @IsValid = 0 THROW 50030, 'Status not valid for Project entity.', 1;

    INSERT dbo.tProject(ProjectNumber, ProjectName, ClientID, UGID, ProjectTypeID, RenewalDate, DueDate, StatusID)
    VALUES (@ProjectNumber, @ProjectName, @ClientID, @UGID, @ProjectTypeID, @RenewalDate, @DueDate, @StatusID);

    SET @NewProjectID = SCOPE_IDENTITY();

    EXEC dbo.usp_ActivityLog_Insert 'Project', @NewProjectID, 'Insert', NULL, NULL, @ProjectNumber;

    SELECT @NewProjectID AS NewProjectID;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Project_Get
    @ProjectID INT
AS
BEGIN
    SET NOCOUNT ON;

    -- dbo.vProject_Current no longer projects StatusID/StatusCode/
    -- StatusDescription or BusinessUnitID/BusinessClassID, so this SP
    -- sources from dbo.vProject directly and joins the descriptive
    -- columns required by ProjectRepository.MapProject.
    SELECT
        p.ProjectID,
        p.ProjectNumber,
        p.ProjectName,
        p.ClientID,
        c.ClientName,
        bc.BusinessClassID,
        bc.BusinessClassName,
        bu.BusinessUnitID,
        bu.BusinessUnitName,
        p.UGID,
        ug.UGName,
        p.ProjectTypeID,
        pt.PTCode,
        pt.PTDescription,
        p.RenewalDate,
        p.DueDate,
        p.StatusID,
        s.StatusCode,
        s.StatusDescription,
        lr.EffectiveDate AS LatestRevenueEffectiveDate,
        lr.Revenue AS LatestRevenue,
        p.IsDeleted,
        p.DeletedDate,
        p.CreatedDate,
        p.ModifiedDate,
        (SELECT COUNT(*) FROM dbo.tProjectService ps WHERE ps.ProjectID = p.ProjectID) AS ServiceCount,
        (SELECT COUNT(*) FROM dbo.tProjectNote pn WHERE pn.ProjectID = p.ProjectID) AS NoteCount
    FROM dbo.vProject p
    JOIN dbo.vClient c ON p.ClientID = c.ClientID
    JOIN dbo.tBusinessClass bc ON c.BusinessClassID = bc.BusinessClassID
    JOIN dbo.tBusinessUnit bu ON bc.BusinessUnitID = bu.BusinessUnitID
    JOIN dbo.vUserGroup ug ON p.UGID = ug.UGID
    JOIN dbo.tProjectType pt ON p.ProjectTypeID = pt.ProjectTypeID
    JOIN dbo.tStatus s ON p.StatusID = s.StatusID
    OUTER APPLY
    (
        SELECT TOP (1) pr.EffectiveDate, pr.Revenue
        FROM dbo.tProjectRevenue pr
        WHERE pr.ProjectID = p.ProjectID
        ORDER BY pr.EffectiveDate DESC, pr.RevenueID DESC
    ) lr
    WHERE p.ProjectID = @ProjectID;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Project_Update
    @ProjectID INT,
    @ProjectNumber VARCHAR(50) = NULL,
    @ProjectName VARCHAR(200) = NULL,
    @UGID INT = NULL,
    @ProjectTypeID INT = NULL,
    @RenewalDate DATE = NULL,
    @DueDate DATE = NULL,
    @StatusID INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.vProject WHERE ProjectID = @ProjectID)
        THROW 50031, 'Project not found or deleted.', 1;
    IF @ProjectNumber IS NOT NULL AND EXISTS (SELECT 1 FROM dbo.tProject WHERE ProjectNumber = @ProjectNumber AND ProjectID <> @ProjectID AND IsDeleted = 0)
        THROW 50032, 'Project number already exists.', 1;
    IF @StatusID IS NOT NULL
    BEGIN
        DECLARE @IsValid BIT;
        EXEC dbo.usp_Status_ValidateForEntity @StatusID, 'Project', @IsValid OUTPUT;
        IF @IsValid = 0 THROW 50033, 'Status not valid for Project entity.', 1;
    END

    UPDATE dbo.tProject
    SET ProjectNumber = COALESCE(@ProjectNumber, ProjectNumber),
        ProjectName = COALESCE(@ProjectName, ProjectName),
        UGID = COALESCE(@UGID, UGID),
        ProjectTypeID = COALESCE(@ProjectTypeID, ProjectTypeID),
        RenewalDate = COALESCE(@RenewalDate, RenewalDate),
        DueDate = COALESCE(@DueDate, DueDate),
        StatusID = COALESCE(@StatusID, StatusID),
        ModifiedDate = SYSUTCDATETIME()
    WHERE ProjectID = @ProjectID;

    EXEC dbo.usp_ActivityLog_Insert 'Project', @ProjectID, 'Update', NULL, NULL, NULL;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Project_List
    @ClientID INT = NULL,
    @UGID INT = NULL,
    @ProjectTypeID INT = NULL,
    @StatusID INT = NULL,
    @DueDateFrom DATE = NULL,
    @DueDateTo DATE = NULL,
    @PageNumber INT = 1,
    @PageSize INT = 50
AS
BEGIN
    SET NOCOUNT ON;
    IF @PageNumber < 1 SET @PageNumber = 1;
    IF @PageSize < 1 SET @PageSize = 50;

    -- dbo.vProject_Current intentionally drops StatusID/StatusCode/
    -- StatusDescription, so this SP sources from dbo.vProject (which
    -- exposes every tProject column, plus the ProjectStatus alias) and
    -- joins descriptive lookup columns required by the
    -- ProjectRepository.MapProject mapper.
    SELECT
        p.ProjectID,
        p.ProjectNumber,
        p.ProjectName,
        p.ClientID,
        c.ClientName,
        bc.BusinessClassID,
        bc.BusinessClassName,
        bu.BusinessUnitID,
        bu.BusinessUnitName,
        p.UGID,
        ug.UGName,
        p.ProjectTypeID,
        pt.PTCode,
        pt.PTDescription,
        p.RenewalDate,
        p.DueDate,
        p.StatusID,
        s.StatusCode,
        s.StatusDescription,
        lr.EffectiveDate AS LatestRevenueEffectiveDate,
        lr.Revenue AS LatestRevenue,
        p.IsDeleted,
        p.DeletedDate,
        p.CreatedDate,
        p.ModifiedDate
    FROM dbo.vProject p
    JOIN dbo.vClient c ON p.ClientID = c.ClientID
    JOIN dbo.tBusinessClass bc ON c.BusinessClassID = bc.BusinessClassID
    JOIN dbo.tBusinessUnit bu ON bc.BusinessUnitID = bu.BusinessUnitID
    JOIN dbo.vUserGroup ug ON p.UGID = ug.UGID
    JOIN dbo.tProjectType pt ON p.ProjectTypeID = pt.ProjectTypeID
    JOIN dbo.tStatus s ON p.StatusID = s.StatusID
    OUTER APPLY
    (
        SELECT TOP (1) pr.EffectiveDate, pr.Revenue
        FROM dbo.tProjectRevenue pr
        WHERE pr.ProjectID = p.ProjectID
        ORDER BY pr.EffectiveDate DESC, pr.RevenueID DESC
    ) lr
    WHERE (@ClientID IS NULL OR p.ClientID = @ClientID)
      AND (@UGID IS NULL OR p.UGID = @UGID)
      AND (@ProjectTypeID IS NULL OR p.ProjectTypeID = @ProjectTypeID)
      AND (@StatusID IS NULL OR p.StatusID = @StatusID)
      AND (@DueDateFrom IS NULL OR p.DueDate >= @DueDateFrom)
      AND (@DueDateTo IS NULL OR p.DueDate <= @DueDateTo)
    ORDER BY p.DueDate DESC, p.ProjectNumber
    OFFSET (@PageNumber - 1) * @PageSize ROWS FETCH NEXT @PageSize ROWS ONLY;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Project_SoftDelete
    @ProjectID INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.tProject
    SET IsDeleted = 1, DeletedDate = SYSUTCDATETIME(), ModifiedDate = SYSUTCDATETIME()
    WHERE ProjectID = @ProjectID AND IsDeleted = 0;

    IF @@ROWCOUNT = 0 THROW 50034, 'Project not found or already deleted.', 1;

    EXEC dbo.usp_ActivityLog_Insert 'Project', @ProjectID, 'SoftDelete', NULL, NULL, NULL;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Project_Restore
    @ProjectID INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @ProjectNumber VARCHAR(50);
    SELECT @ProjectNumber = ProjectNumber FROM dbo.tProject WHERE ProjectID = @ProjectID AND IsDeleted = 1;

    IF @ProjectNumber IS NULL THROW 50035, 'Deleted project not found.', 1;
    IF EXISTS (SELECT 1 FROM dbo.tProject WHERE ProjectNumber = @ProjectNumber AND IsDeleted = 0)
        THROW 50036, 'Cannot restore: active project with same ProjectNumber exists.', 1;

    UPDATE dbo.tProject
    SET IsDeleted = 0, DeletedDate = NULL, ModifiedDate = SYSUTCDATETIME()
    WHERE ProjectID = @ProjectID;

    EXEC dbo.usp_ActivityLog_Insert 'Project', @ProjectID, 'Restore', NULL, NULL, NULL;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Project_AddRevenue
    @ProjectID INT,
    @EffectiveDate DATE = NULL,
    @Revenue DECIMAL(18,2),
    @ChangeReason VARCHAR(255) = NULL,
    @RecordedBy INT,
    @NewRevenueID INT = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    IF @EffectiveDate IS NULL SET @EffectiveDate = CONVERT(DATE, SYSUTCDATETIME());
    IF NOT EXISTS (SELECT 1 FROM dbo.vProject WHERE ProjectID = @ProjectID)
        THROW 50037, 'Project not found or deleted.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.vUser WHERE UserID = @RecordedBy)
        THROW 50038, 'RecordedBy user not found or deleted.', 1;

    INSERT dbo.tProjectRevenue(ProjectID, EffectiveDate, Revenue, ChangeReason, RecordedBy)
    VALUES (@ProjectID, @EffectiveDate, @Revenue, @ChangeReason, @RecordedBy);

    SET @NewRevenueID = SCOPE_IDENTITY();
    EXEC dbo.usp_ActivityLog_Insert 'Project', @ProjectID, 'AddRevenue', 'Revenue', NULL, @Revenue;

    SELECT @NewRevenueID AS NewRevenueID;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Project_GetRevenueHistory
    @ProjectID INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT pr.*, CONCAT(u.FirstName, ' ', u.LastName) AS RecordedByName
    FROM dbo.tProjectRevenue pr
    JOIN dbo.tUser u ON pr.RecordedBy = u.UserID
    WHERE pr.ProjectID = @ProjectID
    ORDER BY pr.EffectiveDate DESC, pr.RevenueID DESC;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Project_AddService
    @ProjectID INT,
    @ServiceID INT,
    @Frequency VARCHAR(20),
    @StartDate DATE,
    @EndDate DATE = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM dbo.vProject WHERE ProjectID = @ProjectID)
        THROW 50039, 'Project not found or deleted.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.vService WHERE ServiceID = @ServiceID)
        THROW 50040, 'Service not found or deleted.', 1;

    INSERT dbo.tProjectService(ProjectID, ServiceID, Frequency, StartDate, EndDate)
    VALUES (@ProjectID, @ServiceID, @Frequency, @StartDate, @EndDate);

    EXEC dbo.usp_ActivityLog_Insert 'Project', @ProjectID, 'AddService', 'ServiceID', NULL, @ServiceID;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Project_RemoveService
    @ProjectID INT,
    @ServiceID INT
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.tProjectService WHERE ProjectID = @ProjectID AND ServiceID = @ServiceID;
    EXEC dbo.usp_ActivityLog_Insert 'Project', @ProjectID, 'RemoveService', 'ServiceID', @ServiceID, NULL;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Project_AddNote
    @ProjectID INT,
    @UserID INT,
    @NoteType VARCHAR(30),
    @NoteText NVARCHAR(MAX),
    @IsPinned BIT = 0,
    @NewNoteID INT = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM dbo.vProject WHERE ProjectID = @ProjectID)
        THROW 50041, 'Project not found or deleted.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.vUser WHERE UserID = @UserID)
        THROW 50042, 'User not found or deleted.', 1;

    INSERT dbo.tProjectNote(ProjectID, UserID, NoteType, NoteText, IsPinned)
    VALUES (@ProjectID, @UserID, @NoteType, @NoteText, @IsPinned);

    SET @NewNoteID = SCOPE_IDENTITY();
    EXEC dbo.usp_ActivityLog_Insert 'Project', @ProjectID, 'AddNote', 'NoteID', NULL, @NewNoteID;

    SELECT @NewNoteID AS NewNoteID;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Project_UpdateNote
    @NoteID INT,
    @NoteText NVARCHAR(MAX) = NULL,
    @NoteType VARCHAR(30) = NULL,
    @IsPinned BIT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.tProjectNote
    SET NoteText = COALESCE(@NoteText, NoteText),
        NoteType = COALESCE(@NoteType, NoteType),
        IsPinned = COALESCE(@IsPinned, IsPinned),
        ModifiedDate = SYSUTCDATETIME()
    WHERE NoteID = @NoteID;

    IF @@ROWCOUNT = 0 THROW 50043, 'Note not found.', 1;
    EXEC dbo.usp_ActivityLog_Insert 'ProjectNote', @NoteID, 'Update', NULL, NULL, NULL;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Project_DeleteNote
    @NoteID INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @ProjectID INT;
    SELECT @ProjectID = ProjectID FROM dbo.tProjectNote WHERE NoteID = @NoteID;
    DELETE FROM dbo.tProjectNote WHERE NoteID = @NoteID;
    EXEC dbo.usp_ActivityLog_Insert 'Project', @ProjectID, 'DeleteNote', 'NoteID', @NoteID, NULL;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Project_ListNotes
    @ProjectID INT,
    @NoteType VARCHAR(30) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT pn.*, CONCAT(u.FirstName, ' ', u.LastName) AS UserName
    FROM dbo.tProjectNote pn
    JOIN dbo.tUser u ON pn.UserID = u.UserID
    WHERE pn.ProjectID = @ProjectID
      AND (@NoteType IS NULL OR pn.NoteType = @NoteType)
    ORDER BY pn.IsPinned DESC, pn.NoteDate DESC;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Project_GetFull
    @ProjectID INT
AS
BEGIN
    SET NOCOUNT ON;

    EXEC dbo.usp_Project_Get @ProjectID;

    SELECT s.ServiceID, s.ServiceName, sc.CategoryName, ps.Frequency, ps.StartDate, ps.EndDate, s.IsDeleted
    FROM dbo.tProjectService ps
    JOIN dbo.tService s ON ps.ServiceID = s.ServiceID
    JOIN dbo.tServiceCategory sc ON s.ServiceCategoryID = sc.ServiceCategoryID
    WHERE ps.ProjectID = @ProjectID
    ORDER BY sc.CategoryName, s.ServiceName;

    SELECT TOP (5) *
    FROM dbo.tProjectNote
    WHERE ProjectID = @ProjectID
    ORDER BY IsPinned DESC, NoteDate DESC;

    SELECT TOP (5) *
    FROM dbo.tActivityLog
    WHERE EntityType = 'Project' AND EntityID = @ProjectID
    ORDER BY ActionDate DESC;
END;
GO

--vProject_Current - WIll be used for the main project List
CREATE OR ALTER PROCEDURE [dbo].[usp_Create_vProject_Current]
AS
BEGIN
    SET NOCOUNT ON;

	--exec usp_Create_vProject_Current

    DECLARE @columns NVARCHAR(MAX);
    DECLARE @pivotColumns NVARCHAR(MAX);
    DECLARE @colServices NVARCHAR(MAX);
    DECLARE @roleSelect NVARCHAR(MAX) = N'';
    DECLARE @roleJoin NVARCHAR(MAX) = N'';
    DECLARE @serviceSelect NVARCHAR(MAX) = N'';
    DECLARE @serviceJoin NVARCHAR(MAX) = N'';
    DECLARE @sql NVARCHAR(MAX);

    SELECT @columns = STRING_AGG('ISNULL(roleUsers.' + QUOTENAME(UserRoleName) + ', ''n/a'') AS ' + QUOTENAME(UserRoleName), ',')
    FROM (
        SELECT DISTINCT UserRoleName
        FROM dbo.tUserRole
        WHERE UserRoleName IS NOT NULL
    ) r;

    SELECT @pivotColumns = STRING_AGG(QUOTENAME(UserRoleName), ',')
    FROM (
        SELECT DISTINCT UserRoleName
        FROM dbo.tUserRole
        WHERE UserRoleName IS NOT NULL
    ) r;

    -- Build one column per active user role assigned to the project user group.
    IF @columns IS NOT NULL AND @pivotColumns IS NOT NULL
    BEGIN
        SET @roleSelect = N',
    ' + @columns;

        SET @roleJoin = N'
LEFT JOIN
(
    SELECT UGID, ' + @pivotColumns + N'
    FROM
    (
        SELECT
            ugu.UGID,
            ur.UserRoleName,
            STRING_AGG(CONCAT(u2.FirstName, '' '', u2.LastName), ''; '') AS UserNames
        FROM dbo.tUserGroupUser ugu
			JOIN dbo.vUser u2 ON ugu.UserID = u2.UserID
			JOIN dbo.tUserRole ur ON ugu.UserRoleID = ur.UserRoleID
        GROUP BY ugu.UGID, ur.UserRoleName
    ) src
    PIVOT
    (
        MAX(UserNames)
        FOR UserRoleName IN (' + @pivotColumns + N')
    ) pvt
) roleUsers ON p.UGID = roleUsers.UGID';
    END;

    -- Build one column per service category/function and service assigned to a project.
    -- Current Phase I schema uses dbo.tService and dbo.tServiceCategory.
    -- Column format: [CategoryName_ServiceName].
    SELECT @colServices = STRING_AGG(CatService, ',')
    FROM (
        SELECT DISTINCT
            QUOTENAME(
                LEFT(
                    REPLACE(REPLACE(REPLACE(CONCAT(sc.CategoryName, '_', s.ServiceName), '/', '_'), ' ', '_'), '-', '_'),
                    128
                )
            ) AS CatService
        FROM dbo.tService s
        INNER JOIN dbo.tServiceCategory sc ON s.ServiceCategoryID = sc.ServiceCategoryID
        INNER JOIN dbo.tProjectService ps ON s.ServiceID = ps.ServiceID
        WHERE sc.CatFunction IS NOT NULL AND sc.CategoryName IS NOT NULL AND s.ServiceName IS NOT NULL
    ) s;

    IF @colServices IS NOT NULL
    BEGIN
        SELECT @serviceSelect = STRING_AGG('ISNULL(projectServices.' + CatService + ', ''n/a'') AS ' + CatService, ',')
        FROM (
            SELECT DISTINCT
                QUOTENAME(
                    LEFT(
                        REPLACE(REPLACE(REPLACE(CONCAT(sc.CategoryName, '_', s.ServiceName), '/', '_'), ' ', '_'), '-', '_'),
                        128
                    )
                ) AS CatService
            FROM dbo.tService s
            INNER JOIN dbo.tServiceCategory sc ON s.ServiceCategoryID = sc.ServiceCategoryID
            INNER JOIN dbo.tProjectService ps ON s.ServiceID = ps.ServiceID
            WHERE sc.CatFunction IS NOT NULL AND sc.CategoryName IS NOT NULL AND s.ServiceName IS NOT NULL
        ) s;

        SET @serviceSelect = N',
    ' + @serviceSelect;

        SET @serviceJoin = N'
LEFT JOIN
(
    SELECT ProjectID, ' + @colServices + N'
    FROM
    (
        SELECT
            ps.ProjectID,
            LEFT(REPLACE(REPLACE(REPLACE(CONCAT(sc.CategoryName, ''_'', s.ServiceName), ''/'', ''_''), '' '', ''_''), ''-'', ''_''),128) AS CatService,
            COALESCE(NULLIF(ps.Frequency, ''''), ''Yes'') AS ServiceValue
        FROM dbo.tProjectService ps
        INNER JOIN dbo.tService s ON ps.ServiceID = s.ServiceID
        INNER JOIN dbo.tServiceCategory sc ON s.ServiceCategoryID = sc.ServiceCategoryID
        WHERE sc.CatFunction IS NOT NULL AND sc.CategoryName IS NOT NULL AND s.ServiceName IS NOT NULL
    ) src
    PIVOT
    (
        MAX(ServiceValue)
        FOR CatService IN (' + @colServices + N')
    ) pvt
) projectServices ON p.ProjectID = projectServices.ProjectID';
    END;

    SET @sql = N'
CREATE OR ALTER VIEW dbo.vProject_Current
AS
SELECT
    bu.BusinessUnitName,
    bc.BusinessClassName,
    p.ClientID,
    c.ClientName,
	c.ClientStatus,
    p.ProjectID,
    p.ProjectNumber,
    p.ProjectName,
	pt.PTDescription,	
    p.RenewalDate,
    p.DueDate,
    p.ProjectStatus,
    lr.RevenueID AS LatestRevenueID,
    lr.EffectiveDate AS LatestRevenueEffectiveDate,
    lr.Revenue AS LatestRevenue,
    p.UGID,
    ug.UGName,
    p.CreatedDate,
    p.ModifiedDate'
    + @roleSelect
    + @serviceSelect + N'
FROM dbo.vProject p
	JOIN dbo.vClient c ON p.ClientID = c.ClientID
	JOIN dbo.tBusinessClass bc ON c.BusinessClassID = bc.BusinessClassID
	JOIN dbo.tBusinessUnit bu ON bc.BusinessUnitID = bu.BusinessUnitID
	JOIN dbo.vUserGroup ug ON p.UGID = ug.UGID
	JOIN dbo.tProjectType pt ON p.ProjectTypeID = pt.ProjectTypeID'
    + @roleJoin
    + @serviceJoin + N'
OUTER APPLY
(
    SELECT TOP (1) pr.*
    FROM dbo.tProjectRevenue pr
    WHERE pr.ProjectID = p.ProjectID
    ORDER BY pr.EffectiveDate DESC, pr.RevenueID DESC
) lr;';

    EXEC sys.sp_executesql @sql;
END;
GO


/* ============================================================
   User procedures
   ============================================================ */

CREATE OR ALTER PROCEDURE dbo.usp_User_Add
    @FirstName VARCHAR(50),
    @LastName VARCHAR(50),
    @Title VARCHAR(100) = NULL,
    @Email VARCHAR(150),
    @Phone VARCHAR(30) = NULL,
    @Company VARCHAR(150) = NULL,
    @Department VARCHAR(100) = NULL,
    @UserType VARCHAR(20),
    @StatusID INT,
    @NewUserID INT = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    IF @UserType NOT IN ('Internal','External')
        THROW 50044, 'UserType must be Internal or External.', 1;
    IF EXISTS (SELECT 1 FROM dbo.tUser WHERE Email = @Email AND IsDeleted = 0)
        THROW 50047, 'Email already exists.', 1;

    DECLARE @IsValid BIT;
    EXEC dbo.usp_Status_ValidateForEntity @StatusID, 'User', @IsValid OUTPUT;
    IF @IsValid = 0 THROW 50048, 'Status not valid for User entity.', 1;

    INSERT dbo.tUser(FirstName, LastName, Title, Email, Phone, Company, Department, UserType, StatusID)
    VALUES (@FirstName, @LastName, @Title, @Email, @Phone, @Company, @Department, @UserType, @StatusID);

    SET @NewUserID = SCOPE_IDENTITY();
    EXEC dbo.usp_ActivityLog_Insert 'User', @NewUserID, 'Insert', NULL, NULL, @Email;

    SELECT @NewUserID AS NewUserID;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_User_Get
    @UserID INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        u.*,
        s.StatusCode,
        s.StatusDescription
    FROM dbo.vUser u
    JOIN dbo.tStatus s ON u.StatusID = s.StatusID
    WHERE u.UserID = @UserID;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_User_Update
    @UserID INT,
    @FirstName VARCHAR(50) = NULL,
    @LastName VARCHAR(50) = NULL,
    @Title VARCHAR(100) = NULL,
    @Email VARCHAR(150) = NULL,
    @Phone VARCHAR(30) = NULL,
    @Company VARCHAR(150) = NULL,
    @Department VARCHAR(100) = NULL,
    @UserType VARCHAR(20) = NULL,
    @StatusID INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.vUser WHERE UserID = @UserID)
        THROW 50049, 'User not found or deleted.', 1;
    IF @Email IS NOT NULL AND EXISTS (SELECT 1 FROM dbo.tUser WHERE Email = @Email AND UserID <> @UserID AND IsDeleted = 0)
        THROW 50050, 'Email already exists.', 1;

    DECLARE @FinalUserType VARCHAR(20);
    SELECT @FinalUserType = COALESCE(@UserType, UserType)
    FROM dbo.tUser
    WHERE UserID = @UserID;

    IF @FinalUserType NOT IN ('Internal','External')
        THROW 50051, 'UserType must be Internal or External.', 1;

    IF @StatusID IS NOT NULL
    BEGIN
        DECLARE @IsValid BIT;
        EXEC dbo.usp_Status_ValidateForEntity @StatusID, 'User', @IsValid OUTPUT;
        IF @IsValid = 0 THROW 50054, 'Status not valid for User entity.', 1;
    END

    UPDATE dbo.tUser
    SET FirstName = COALESCE(@FirstName, FirstName),
        LastName = COALESCE(@LastName, LastName),
        Title = COALESCE(@Title, Title),
        Email = COALESCE(@Email, Email),
        Phone = COALESCE(@Phone, Phone),
        Company = COALESCE(@Company, Company),
        Department = COALESCE(@Department, Department),
        UserType = @FinalUserType,
        StatusID = COALESCE(@StatusID, StatusID),
        ModifiedDate = SYSUTCDATETIME()
    WHERE UserID = @UserID;

    EXEC dbo.usp_ActivityLog_Insert 'User', @UserID, 'Update', NULL, NULL, NULL;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_User_List
    @ActiveOnly BIT = 0,
    @UserType VARCHAR(20) = NULL,
    @StatusID INT = NULL,
    @SearchTerm NVARCHAR(150) = NULL,
    @PageNumber INT = 1,
    @PageSize INT = 50
AS
BEGIN
    SET NOCOUNT ON;
    IF @PageNumber < 1 SET @PageNumber = 1;
    IF @PageSize < 1 SET @PageSize = 50;

    SELECT u.*, s.StatusCode, s.StatusDescription
    FROM dbo.vUser u
    JOIN dbo.tStatus s ON u.StatusID = s.StatusID
    WHERE (@ActiveOnly = 0 OR s.StatusCode = 'A')
      AND (@UserType IS NULL OR u.UserType = @UserType)
      AND (@StatusID IS NULL OR u.StatusID = @StatusID)
      AND (
          @SearchTerm IS NULL
          OR u.FirstName LIKE '%' + @SearchTerm + '%'
          OR u.LastName LIKE '%' + @SearchTerm + '%'
          OR u.Email LIKE '%' + @SearchTerm + '%'
      )
    ORDER BY u.LastName, u.FirstName
    OFFSET (@PageNumber - 1) * @PageSize ROWS FETCH NEXT @PageSize ROWS ONLY;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_User_SoftDelete
    @UserID INT
AS
BEGIN
    SET NOCOUNT ON;

    -- Project-level ownership has been removed from the schema, so there
    -- is no longer a "user owns active projects" guard here. Membership in
    -- active user groups is still allowed; cascade rules are managed via
    -- dbo.tUserGroupUser.

    UPDATE dbo.tUser
    SET IsDeleted = 1, DeletedDate = SYSUTCDATETIME(), ModifiedDate = SYSUTCDATETIME()
    WHERE UserID = @UserID AND IsDeleted = 0;

    IF @@ROWCOUNT = 0 THROW 50056, 'User not found or already deleted.', 1;

    EXEC dbo.usp_ActivityLog_Insert 'User', @UserID, 'SoftDelete', NULL, NULL, NULL;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_User_Restore
    @UserID INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Email VARCHAR(150);
    SELECT @Email = Email FROM dbo.tUser WHERE UserID = @UserID AND IsDeleted = 1;
    IF @Email IS NULL THROW 50057, 'Deleted user not found.', 1;
    IF EXISTS (SELECT 1 FROM dbo.tUser WHERE Email = @Email AND IsDeleted = 0)
        THROW 50058, 'Cannot restore: active user with same Email exists.', 1;

    UPDATE dbo.tUser
    SET IsDeleted = 0, DeletedDate = NULL, ModifiedDate = SYSUTCDATETIME()
    WHERE UserID = @UserID;

    EXEC dbo.usp_ActivityLog_Insert 'User', @UserID, 'Restore', NULL, NULL, NULL;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_User_AddAddress
    @UserID INT,
    @AddressType VARCHAR(20),
    @Address1 VARCHAR(200),
    @Address2 VARCHAR(200) = NULL,
    @City VARCHAR(100),
    @State VARCHAR(50) = NULL,
    @PostalCode VARCHAR(20) = NULL,
    @Country VARCHAR(50),
    @NewAddressID INT = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM dbo.vUser WHERE UserID = @UserID)
        THROW 50059, 'User not found or deleted.', 1;

    INSERT dbo.tUserAddress(UserID, AddressType, Address1, Address2, City, State, PostalCode, Country)
    VALUES (@UserID, @AddressType, @Address1, @Address2, @City, @State, @PostalCode, @Country);

    SET @NewAddressID = SCOPE_IDENTITY();
    EXEC dbo.usp_ActivityLog_Insert 'User', @UserID, 'AddAddress', 'AddressID', NULL, @NewAddressID;

    SELECT @NewAddressID AS NewAddressID;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_User_UpdateAddress
    @UserID INT,
    @AddressType VARCHAR(20) = NULL,
    @Address1 VARCHAR(200) = NULL,
    @Address2 VARCHAR(200) = NULL,
    @City VARCHAR(100) = NULL,
    @State VARCHAR(50) = NULL,
    @PostalCode VARCHAR(20) = NULL,
    @Country VARCHAR(50) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM dbo.vUser WHERE UserID = @UserID)
        THROW 50059, 'User not found or deleted.', 1;

    /* One form drives all rows for this user — assign parameters directly (empty string is a valid value). */
    UPDATE dbo.tUserAddress
    SET AddressType = @AddressType,
        Address1 = @Address1,
        Address2 = @Address2,
        City = @City,
        State = @State,
        PostalCode = @PostalCode,
        Country = @Country,
        ModifiedDate = SYSUTCDATETIME()
    WHERE UserID = @UserID;

    IF @@ROWCOUNT = 0
        THROW 50060, 'No address rows found for this user.', 1;

    EXEC dbo.usp_ActivityLog_Insert 'User', @UserID, 'UpdateAddress', NULL, NULL, NULL;

    SELECT @UserID AS UserID;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_User_DeleteAddress
    @AddressID INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @UserID INT;
    SELECT @UserID = UserID FROM dbo.tUserAddress WHERE AddressID = @AddressID;
    DELETE FROM dbo.tUserAddress WHERE AddressID = @AddressID;
    EXEC dbo.usp_ActivityLog_Insert 'User', @UserID, 'DeleteAddress', 'AddressID', @AddressID, NULL;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_User_ListAddresses
    @UserID INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT * FROM dbo.tUserAddress WHERE UserID = @UserID ORDER BY AddressType, AddressID;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_User_GetWorkload
    @UserID INT
AS
BEGIN
    SET NOCOUNT ON;

    -- Project-level ownership has been removed from the schema, so the
    -- only "workload" lookup that remains here is the set of active user
    -- groups the user belongs to. Project counts/status rollups for a
    -- user can be derived later via the user-group -> project linkage if
    -- needed.
    SELECT ug.*
    FROM dbo.vUserGroup ug
    JOIN dbo.tUserGroupUser ugu ON ug.UGID = ugu.UGID
    WHERE ugu.UserID = @UserID;
END;
GO


/* ============================================================
   User group procedures
   ============================================================ */

CREATE OR ALTER PROCEDURE dbo.usp_UserGroup_Add
    @UserGroupNumber VARCHAR(50),
    @UGName VARCHAR(100),
    @UserRoleID INT = NULL,
    @StatusID INT,
    @NewUGID INT = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM dbo.tUserGroup WHERE UserGroupNumber = @UserGroupNumber AND IsDeleted = 0)
        THROW 50061, 'UserGroupNumber already exists.', 1;

    DECLARE @IsValid BIT;
    EXEC dbo.usp_Status_ValidateForEntity @StatusID, 'UserGroup', @IsValid OUTPUT;
    IF @IsValid = 0 THROW 50062, 'Status not valid for UserGroup entity.', 1;

    INSERT dbo.tUserGroup(UserGroupNumber, UGName, UserRoleID, StatusID)
    VALUES (@UserGroupNumber, @UGName, @UserRoleID, @StatusID);

    SET @NewUGID = SCOPE_IDENTITY();
    EXEC dbo.usp_ActivityLog_Insert 'UserGroup', @NewUGID, 'Insert', NULL, NULL, @UserGroupNumber;

    SELECT @NewUGID AS NewUGID;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_UserGroup_Get
    @UGID INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT ug.*, ur.UserRoleName, COUNT(ugu.UserID) AS MemberCount
    FROM dbo.vUserGroup ug
    LEFT JOIN dbo.tUserRole ur ON ug.UserRoleID = ur.UserRoleID
    LEFT JOIN dbo.tUserGroupUser ugu ON ug.UGID = ugu.UGID
    WHERE ug.UGID = @UGID
    GROUP BY ug.UGID, ug.UserGroupNumber, ug.UGName, ug.UserRoleID, ug.StatusID, ug.CreatedDate, ug.ModifiedDate, ug.IsDeleted, ug.DeletedDate, ur.UserRoleName;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_UserGroup_Update
    @UGID INT,
    @UserGroupNumber VARCHAR(50) = NULL,
    @UGName VARCHAR(100) = NULL,
    @UserRoleID INT = NULL,
    @StatusID INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF @UserGroupNumber IS NOT NULL AND EXISTS (SELECT 1 FROM dbo.tUserGroup WHERE UserGroupNumber = @UserGroupNumber AND UGID <> @UGID AND IsDeleted = 0)
        THROW 50063, 'UserGroupNumber already exists.', 1;

    UPDATE dbo.tUserGroup
    SET UserGroupNumber = COALESCE(@UserGroupNumber, UserGroupNumber),
        UGName = COALESCE(@UGName, UGName),
        UserRoleID = COALESCE(@UserRoleID, UserRoleID),
        StatusID = COALESCE(@StatusID, StatusID),
        ModifiedDate = SYSUTCDATETIME()
    WHERE UGID = @UGID AND IsDeleted = 0;

    IF @@ROWCOUNT = 0 THROW 50064, 'UserGroup not found or deleted.', 1;
    EXEC dbo.usp_ActivityLog_Insert 'UserGroup', @UGID, 'Update', NULL, NULL, NULL;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_UserGroup_List
    @SearchTerm NVARCHAR(100) = NULL,
    @IncludeDeleted BIT = 0,
    @UserRoleID INT = NULL,
    @StatusID INT = NULL,
    @PageNumber INT = 1,
    @PageSize INT = 50
AS
BEGIN
    SET NOCOUNT ON;
    IF @PageNumber < 1 SET @PageNumber = 1;
    IF @PageSize < 1 SET @PageSize = 50;

    SELECT
        ug.UGID,
        ug.UserGroupNumber,
        ug.UGName,
        ug.UserRoleID,
        ur.UserRoleName,
        ug.StatusID,
        s.StatusCode,
        s.StatusDescription,
        ug.IsDeleted,
        ug.DeletedDate,
        ug.CreatedDate,
        ug.ModifiedDate,
        (SELECT COUNT(1) FROM dbo.tUserGroupUser ugu WHERE ugu.UGID = ug.UGID) AS MemberCount
    FROM dbo.tUserGroup ug
    LEFT JOIN dbo.tUserRole ur ON ug.UserRoleID = ur.UserRoleID
    JOIN dbo.tStatus s ON ug.StatusID = s.StatusID
    WHERE (@IncludeDeleted = 1 OR ug.IsDeleted = 0)
      AND (@UserRoleID IS NULL OR ug.UserRoleID = @UserRoleID)
      AND (@StatusID IS NULL OR ug.StatusID = @StatusID)
      AND (
          @SearchTerm IS NULL
          OR ug.UGName LIKE '%' + @SearchTerm + '%'
          OR ug.UserGroupNumber LIKE '%' + @SearchTerm + '%'
      )
    ORDER BY ug.UserGroupNumber
    OFFSET (@PageNumber - 1) * @PageSize ROWS FETCH NEXT @PageSize ROWS ONLY;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_UserGroup_SoftDelete
    @UGID INT
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM dbo.vProject WHERE UGID = @UGID)
        THROW 50065, 'Cannot delete: user group is referenced by active projects.', 1;

    UPDATE dbo.tUserGroup
    SET IsDeleted = 1, DeletedDate = SYSUTCDATETIME(), ModifiedDate = SYSUTCDATETIME()
    WHERE UGID = @UGID AND IsDeleted = 0;

    IF @@ROWCOUNT = 0 THROW 50066, 'UserGroup not found or already deleted.', 1;
    EXEC dbo.usp_ActivityLog_Insert 'UserGroup', @UGID, 'SoftDelete', NULL, NULL, NULL;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_UserGroup_Restore
    @UGID INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @UserGroupNumber VARCHAR(50);
    SELECT @UserGroupNumber = UserGroupNumber FROM dbo.tUserGroup WHERE UGID = @UGID AND IsDeleted = 1;
    IF @UserGroupNumber IS NULL THROW 50067, 'Deleted UserGroup not found.', 1;
    IF EXISTS (SELECT 1 FROM dbo.tUserGroup WHERE UserGroupNumber = @UserGroupNumber AND IsDeleted = 0)
        THROW 50068, 'Cannot restore: active UserGroup with same UserGroupNumber exists.', 1;

    UPDATE dbo.tUserGroup
    SET IsDeleted = 0, DeletedDate = NULL, ModifiedDate = SYSUTCDATETIME()
    WHERE UGID = @UGID;

    EXEC dbo.usp_ActivityLog_Insert 'UserGroup', @UGID, 'Restore', NULL, NULL, NULL;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_UserGroup_AddMember
    @UGID INT,
    @UserID INT,
    @UserRoleID INT = NULL,
    @StatusID INT
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM dbo.vUserGroup WHERE UGID = @UGID)
        THROW 50069, 'UserGroup not found or deleted.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.vUser WHERE UserID = @UserID)
        THROW 50070, 'User not found or deleted.', 1;

    INSERT dbo.tUserGroupUser(UGID, UserID, UserRoleID, StatusID)
    VALUES (@UGID, @UserID, @UserRoleID, @StatusID);

    EXEC dbo.usp_ActivityLog_Insert 'UserGroup', @UGID, 'AddMember', 'UserID', NULL, @UserID;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_UserGroup_RemoveMember
    @UGID INT,
    @UserID INT
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.tUserGroupUser WHERE UGID = @UGID AND UserID = @UserID;
    EXEC dbo.usp_ActivityLog_Insert 'UserGroup', @UGID, 'RemoveMember', 'UserID', @UserID, NULL;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_UserGroup_UpdateMember
    @UGID INT,
    @UserID INT,
    @UserRoleID INT = NULL,
    @StatusID INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.tUserGroupUser
    SET UserRoleID = COALESCE(@UserRoleID, UserRoleID),
        StatusID = COALESCE(@StatusID, StatusID)
    WHERE UGID = @UGID AND UserID = @UserID;

    IF @@ROWCOUNT = 0 THROW 50071, 'UserGroup member not found.', 1;
    EXEC dbo.usp_ActivityLog_Insert 'UserGroup', @UGID, 'UpdateMember', 'UserID', @UserID, @UserID;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_UserGroup_ListMembers
    @UGID INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        ugu.UGID,
        ugu.UserID,
        u.FirstName,
        u.LastName,
        u.Email,
        ugu.UserRoleID,
        ur.UserRoleName,
        ugu.StatusID,
        s.StatusCode,
        ugu.JoinedDate
    FROM dbo.tUserGroupUser ugu
    JOIN dbo.vUser u ON ugu.UserID = u.UserID
    LEFT JOIN dbo.tUserRole ur ON ugu.UserRoleID = ur.UserRoleID
    JOIN dbo.tStatus s ON ugu.StatusID = s.StatusID
    WHERE ugu.UGID = @UGID
    ORDER BY u.LastName, u.FirstName;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_User_ListGroups
    @UserID INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT ug.*
    FROM dbo.vUserGroup ug
    JOIN dbo.tUserGroupUser ugu ON ug.UGID = ugu.UGID
    WHERE ugu.UserID = @UserID
    ORDER BY ug.UserGroupNumber;
END;
GO

/* ============================================================
   Service procedures
   ============================================================ */

CREATE OR ALTER PROCEDURE dbo.usp_Service_Add
    @ServiceName VARCHAR(150),
    @ServiceDescription VARCHAR(255) = NULL,
    @ServiceCategoryID INT,
    @StatusID INT,
    @NewServiceID INT = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (SELECT 1 FROM dbo.tService WHERE ServiceName = @ServiceName AND IsDeleted = 0)
        THROW 50072, 'ServiceName already exists.', 1;

    DECLARE @IsValid BIT;
    EXEC dbo.usp_Status_ValidateForEntity @StatusID, 'Service', @IsValid OUTPUT;
    IF @IsValid = 0 THROW 50073, 'Status not valid for Service entity.', 1;

    INSERT dbo.tService(ServiceName, ServiceDescription, ServiceCategoryID, StatusID)
    VALUES (@ServiceName, @ServiceDescription, @ServiceCategoryID, @StatusID);

    SET @NewServiceID = SCOPE_IDENTITY();
    EXEC dbo.usp_ActivityLog_Insert 'Service', @NewServiceID, 'Insert', NULL, NULL, @ServiceName;

    SELECT @NewServiceID AS NewServiceID;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Service_Get
    @ServiceID INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT s.*, sc.CategoryName, sc.CatFunction, st.StatusCode,
           STRING_AGG(t.TagName, ', ') AS Tags
    FROM dbo.vService s
    JOIN dbo.tServiceCategory sc ON s.ServiceCategoryID = sc.ServiceCategoryID
    JOIN dbo.tStatus st ON s.StatusID = st.StatusID
    LEFT JOIN dbo.tServiceTag stg ON s.ServiceID = stg.ServiceID
    LEFT JOIN dbo.tTag t ON stg.TagID = t.TagID
    WHERE s.ServiceID = @ServiceID
    GROUP BY s.ServiceID, s.ServiceName, s.ServiceDescription, s.ServiceCategoryID, s.StatusID, s.CreatedDate, s.ModifiedDate, s.IsDeleted, s.DeletedDate, sc.CategoryName, sc.CatFunction, st.StatusCode;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Service_Update
    @ServiceID INT,
    @ServiceName VARCHAR(150) = NULL,
    @ServiceDescription VARCHAR(255) = NULL,
    @ServiceCategoryID INT = NULL,
    @StatusID INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF @ServiceName IS NOT NULL AND EXISTS (SELECT 1 FROM dbo.tService WHERE ServiceName = @ServiceName AND ServiceID <> @ServiceID AND IsDeleted = 0)
        THROW 50074, 'ServiceName already exists.', 1;

    UPDATE dbo.tService
    SET ServiceName = COALESCE(@ServiceName, ServiceName),
        ServiceDescription = COALESCE(@ServiceDescription, ServiceDescription),
        ServiceCategoryID = COALESCE(@ServiceCategoryID, ServiceCategoryID),
        StatusID = COALESCE(@StatusID, StatusID),
        ModifiedDate = SYSUTCDATETIME()
    WHERE ServiceID = @ServiceID AND IsDeleted = 0;

    IF @@ROWCOUNT = 0 THROW 50075, 'Service not found or deleted.', 1;
    EXEC dbo.usp_ActivityLog_Insert 'Service', @ServiceID, 'Update', NULL, NULL, NULL;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Service_List
    @IncludeDeleted BIT = 0,
    @SearchTerm NVARCHAR(150) = NULL,
    @ServiceCategoryID INT = NULL,
    @StatusID INT = NULL,
    @TagID INT = NULL,
    @PageNumber INT = 1,
    @PageSize INT = 50
AS
BEGIN
    SET NOCOUNT ON;
    IF @PageNumber < 1 SET @PageNumber = 1;
    IF @PageSize < 1 SET @PageSize = 50;

    -- Sources from dbo.tService directly so soft-deleted rows can be opted
    -- in via @IncludeDeleted = 1. CategoryName, CatFunction, StatusCode and
    -- StatusDescription are joined to satisfy AdminRepository.MapService.
    SELECT DISTINCT
        s.ServiceID,
        s.ServiceName,
        s.ServiceDescription,
        s.ServiceCategoryID,
        sc.CategoryName,
        sc.CatFunction,
        s.StatusID,
        st.StatusCode,
        st.StatusDescription,
        s.IsDeleted,
        s.DeletedDate,
        s.CreatedDate,
        s.ModifiedDate
    FROM dbo.tService s
    JOIN dbo.tServiceCategory sc ON s.ServiceCategoryID = sc.ServiceCategoryID
    JOIN dbo.tStatus st ON s.StatusID = st.StatusID
    LEFT JOIN dbo.tServiceTag stg ON s.ServiceID = stg.ServiceID
    WHERE (@IncludeDeleted = 1 OR s.IsDeleted = 0)
      AND (@ServiceCategoryID IS NULL OR s.ServiceCategoryID = @ServiceCategoryID)
      AND (@StatusID IS NULL OR s.StatusID = @StatusID)
      AND (@TagID IS NULL OR stg.TagID = @TagID)
      AND (
          @SearchTerm IS NULL
          OR s.ServiceName LIKE '%' + @SearchTerm + '%'
          OR s.ServiceDescription LIKE '%' + @SearchTerm + '%'
      )
    ORDER BY s.ServiceName
    OFFSET (@PageNumber - 1) * @PageSize ROWS FETCH NEXT @PageSize ROWS ONLY;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Service_SoftDelete
    @ServiceID INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.tService
    SET IsDeleted = 1, DeletedDate = SYSUTCDATETIME(), ModifiedDate = SYSUTCDATETIME()
    WHERE ServiceID = @ServiceID AND IsDeleted = 0;

    IF @@ROWCOUNT = 0 THROW 50076, 'Service not found or already deleted.', 1;
    EXEC dbo.usp_ActivityLog_Insert 'Service', @ServiceID, 'SoftDelete', NULL, NULL, NULL;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Service_Restore
    @ServiceID INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @ServiceName VARCHAR(150);
    SELECT @ServiceName = ServiceName FROM dbo.tService WHERE ServiceID = @ServiceID AND IsDeleted = 1;
    IF @ServiceName IS NULL THROW 50077, 'Deleted Service not found.', 1;
    IF EXISTS (SELECT 1 FROM dbo.tService WHERE ServiceName = @ServiceName AND IsDeleted = 0)
        THROW 50078, 'Cannot restore: active Service with same ServiceName exists.', 1;

    UPDATE dbo.tService
    SET IsDeleted = 0, DeletedDate = NULL, ModifiedDate = SYSUTCDATETIME()
    WHERE ServiceID = @ServiceID;

    EXEC dbo.usp_ActivityLog_Insert 'Service', @ServiceID, 'Restore', NULL, NULL, NULL;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Service_AddTag
    @ServiceID INT,
    @TagID INT
AS
BEGIN
    SET NOCOUNT ON;
    INSERT dbo.tServiceTag(ServiceID, TagID) VALUES (@ServiceID, @TagID);
    EXEC dbo.usp_ActivityLog_Insert 'Service', @ServiceID, 'AddTag', 'TagID', NULL, @TagID;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Service_RemoveTag
    @ServiceID INT,
    @TagID INT
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.tServiceTag WHERE ServiceID = @ServiceID AND TagID = @TagID;
    EXEC dbo.usp_ActivityLog_Insert 'Service', @ServiceID, 'RemoveTag', 'TagID', @TagID, NULL;
END;
GO

/* ============================================================
   Activity log and revenue report procedures
   ============================================================ */

CREATE OR ALTER PROCEDURE dbo.usp_ActivityLog_GetByEntity
    @EntityType VARCHAR(50),
    @EntityID INT,
    @Top INT = 100,
    @FromDate DATETIME = NULL,
    @ToDate DATETIME = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF @Top IS NULL OR @Top < 1 SET @Top = 100;

    SELECT TOP (@Top)
        al.*
    FROM dbo.tActivityLog al
    WHERE al.EntityType = @EntityType
      AND al.EntityID = @EntityID
      AND (@FromDate IS NULL OR al.ActionDate >= @FromDate)
      AND (@ToDate IS NULL OR al.ActionDate <= @ToDate)
    ORDER BY al.ActionDate DESC;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Revenue_TotalsByPeriod
    @FromDate DATE,
    @ToDate DATE,
    @ClientID INT = NULL,
    @BusinessUnitID INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    ;WITH LatestInWindow AS
    (
        SELECT
            p.ProjectID,
            p.ClientID,
            vc.BusinessUnitID,
            pr.Revenue,
            pr.EffectiveDate,
            ROW_NUMBER() OVER (PARTITION BY p.ProjectID ORDER BY pr.EffectiveDate DESC, pr.RevenueID DESC) AS rn
        FROM dbo.vProject p
        JOIN dbo.vClient_WithBU vc ON p.ClientID = vc.ClientID
        JOIN dbo.tProjectRevenue pr ON p.ProjectID = pr.ProjectID
        WHERE pr.EffectiveDate BETWEEN @FromDate AND @ToDate
          AND (@ClientID IS NULL OR p.ClientID = @ClientID)
          AND (@BusinessUnitID IS NULL OR vc.BusinessUnitID = @BusinessUnitID)
    )
    SELECT BusinessUnitID, ClientID, SUM(Revenue) AS TotalRevenue, COUNT(*) AS ProjectCount
    FROM LatestInWindow
    WHERE rn = 1
    GROUP BY BusinessUnitID, ClientID;
END;
GO


/* ============================================================
   Tear-down for the deployment-mode switch.
   #DeploySettings is session-scoped, but dropping it explicitly
   keeps repeated runs of this file in the same SSMS session clean.
   ============================================================ */
IF OBJECT_ID('tempdb..#DeploySettings') IS NOT NULL
    DROP TABLE #DeploySettings;
GO
