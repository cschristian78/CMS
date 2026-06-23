
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

IF OBJECT_ID(N'dbo.tBusinessUnit', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tBusinessUnit
    (
        BusinessUnitID INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tBusinessUnit PRIMARY KEY,
        BUName VARCHAR(100) NOT NULL,
        BUDescription VARCHAR(255) NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_tBusinessUnit_IsActive DEFAULT (1),
        CreatedDate DATETIME2(0) NOT NULL CONSTRAINT DF_tBusinessUnit_CreatedDate DEFAULT (SYSUTCDATETIME()),
        ModifiedDate DATETIME2(0) NULL,
        CONSTRAINT UQ_tBusinessUnit_BUName UNIQUE (BUName)
    );
END;
GO

IF OBJECT_ID(N'dbo.tBusinessClass', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tBusinessClass
    (
        BusinessClassID INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tBusinessClass PRIMARY KEY,
        BCName VARCHAR(100) NOT NULL,
        BCDescription VARCHAR(255) NULL,
        BusinessUnitID INT NOT NULL,
        CreatedDate DATETIME2(0) NOT NULL CONSTRAINT DF_tBusinessClass_CreatedDate DEFAULT (SYSUTCDATETIME()),
        ModifiedDate DATETIME2(0) NULL,
        CONSTRAINT UQ_tBusinessClass_BusinessUnitID_BCName UNIQUE (BusinessUnitID, BCName),
        CONSTRAINT FK_tBusinessClass_tBusinessUnit FOREIGN KEY (BusinessUnitID)
            REFERENCES dbo.tBusinessUnit(BusinessUnitID) ON DELETE NO ACTION
    );
    CREATE INDEX IX_tBusinessClass_BusinessUnitID ON dbo.tBusinessClass(BusinessUnitID);
END;
GO

IF OBJECT_ID(N'dbo.tClientCategory', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tClientCategory
    (
        ClientCategoryID INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tClientCategory PRIMARY KEY,
        CCName VARCHAR(100) NOT NULL,
        CCDescription VARCHAR(255) NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_tClientCategory_IsActive DEFAULT (1),
        CreatedDate DATETIME2(0) NOT NULL CONSTRAINT DF_tClientCategory_CreatedDate DEFAULT (SYSUTCDATETIME()),
        ModifiedDate DATETIME2(0) NULL,
        CONSTRAINT UQ_tClientCategory_CCName UNIQUE (CCName)
    );
END;
GO

IF OBJECT_ID(N'dbo.tClientType', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tClientType
    (
        ClientTypeID INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tClientType PRIMARY KEY,
        CTName VARCHAR(100) NOT NULL,
        CTDescription VARCHAR(255) NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_tClientType_IsActive DEFAULT (1),
        CreatedDate DATETIME2(0) NOT NULL CONSTRAINT DF_tClientType_CreatedDate DEFAULT (SYSUTCDATETIME()),
        ModifiedDate DATETIME2(0) NULL,
        CONSTRAINT UQ_tClientType_CTName UNIQUE (CTName)
    );
END;
GO

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
GO

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
GO

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
GO

IF OBJECT_ID(N'dbo.tProjectType', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tProjectType
    (
        ProjectTypeID INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tProjectType PRIMARY KEY,
        PTCode VARCHAR(20) NOT NULL,
        PTDescription VARCHAR(255) NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_tProjectType_IsActive DEFAULT (1),
        CreatedDate DATETIME2(0) NOT NULL CONSTRAINT DF_tProjectType_CreatedDate DEFAULT (SYSUTCDATETIME()),
        ModifiedDate DATETIME2(0) NULL,
        CONSTRAINT UQ_tProjectType_PTCode UNIQUE (PTCode)
    );
END;
GO

IF OBJECT_ID(N'dbo.tServiceCategory', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tServiceCategory
    (
        ServiceCategoryID INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tServiceCategory PRIMARY KEY,
        CategoryName VARCHAR(100) NOT NULL,
        CatFunction VARCHAR(100) NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_tServiceCategory_IsActive DEFAULT (1),
        CreatedDate DATETIME2(0) NOT NULL CONSTRAINT DF_tServiceCategory_CreatedDate DEFAULT (SYSUTCDATETIME()),
        ModifiedDate DATETIME2(0) NULL,
        CONSTRAINT UQ_tServiceCategory_CategoryName UNIQUE (CategoryName)
    );
END;
GO

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
        IsActive BIT NOT NULL CONSTRAINT DF_tUserRole_IsActive DEFAULT (1),
        CreatedDate DATETIME2(0) NOT NULL CONSTRAINT DF_tUserRole_CreatedDate DEFAULT (SYSUTCDATETIME()),
        ModifiedDate DATETIME2(0) NULL,
        CONSTRAINT UQ_tUserRole_UserRoleName UNIQUE (UserRoleName),
        CONSTRAINT FK_tUserRole_tStatus FOREIGN KEY (StatusID)
            REFERENCES dbo.tStatus(StatusID) ON DELETE NO ACTION
    );
    CREATE INDEX IX_tUserRole_StatusID ON dbo.tUserRole(StatusID);
END;
GO

IF OBJECT_ID(N'dbo.tTag', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tTag
    (
        TagID INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tTag PRIMARY KEY,
        TagName VARCHAR(50) NOT NULL,
        TagScope VARCHAR(30) NOT NULL,
        CreatedDate DATETIME2(0) NOT NULL CONSTRAINT DF_tTag_CreatedDate DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT UQ_tTag_TagName_TagScope UNIQUE (TagName, TagScope)
    );
END;
GO

/* ============================================================
   02. Business entities
   ============================================================ */

IF OBJECT_ID(N'dbo.tClient', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tClient
    (
        ClientID INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tClient PRIMARY KEY,
        ClientName VARCHAR(150) NOT NULL,
        ClientShortName VARCHAR(50) NULL,
        BusinessClassID INT NOT NULL,
        ClientCategoryID INT NOT NULL,
        ClientTypeID INT NOT NULL,
        StatusID INT NOT NULL,
        CreatedDate DATETIME2(0) NOT NULL CONSTRAINT DF_tClient_CreatedDate DEFAULT (SYSUTCDATETIME()),
        ModifiedDate DATETIME2(0) NULL,
        IsDeleted BIT NOT NULL CONSTRAINT DF_tClient_IsDeleted DEFAULT (0),
        DeletedDate DATETIME2(0) NULL,
        CONSTRAINT FK_tClient_tBusinessClass FOREIGN KEY (BusinessClassID)
            REFERENCES dbo.tBusinessClass(BusinessClassID) ON DELETE NO ACTION,
        CONSTRAINT FK_tClient_tClientCategory FOREIGN KEY (ClientCategoryID)
            REFERENCES dbo.tClientCategory(ClientCategoryID) ON DELETE NO ACTION,
        CONSTRAINT FK_tClient_tClientType FOREIGN KEY (ClientTypeID)
            REFERENCES dbo.tClientType(ClientTypeID) ON DELETE NO ACTION,
        CONSTRAINT FK_tClient_tStatus FOREIGN KEY (StatusID)
            REFERENCES dbo.tStatus(StatusID) ON DELETE NO ACTION
    );
    CREATE UNIQUE INDEX UQ_tClient_ClientName_Active ON dbo.tClient(ClientName) WHERE IsDeleted = 0;
    CREATE INDEX IX_tClient_BusinessClassID ON dbo.tClient(BusinessClassID);
    CREATE INDEX IX_tClient_ClientCategoryID ON dbo.tClient(ClientCategoryID);
    CREATE INDEX IX_tClient_ClientTypeID ON dbo.tClient(ClientTypeID);
    CREATE INDEX IX_tClient_StatusID ON dbo.tClient(StatusID);
END;
GO

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
        BusinessUnitID INT NULL,
        ClientID INT NULL,
        StatusID INT NOT NULL,
        CreatedDate DATETIME2(0) NOT NULL CONSTRAINT DF_tUser_CreatedDate DEFAULT (SYSUTCDATETIME()),
        ModifiedDate DATETIME2(0) NULL,
        IsDeleted BIT NOT NULL CONSTRAINT DF_tUser_IsDeleted DEFAULT (0),
        DeletedDate DATETIME2(0) NULL,
        CONSTRAINT CK_tUser_UserType CHECK (UserType IN ('Internal','External')),
        CONSTRAINT CK_tUser_UserTypeXor CHECK
        (
            (UserType = 'Internal' AND BusinessUnitID IS NOT NULL AND ClientID IS NULL)
            OR
            (UserType = 'External' AND ClientID IS NOT NULL AND BusinessUnitID IS NULL)
        ),
        CONSTRAINT FK_tUser_tBusinessUnit FOREIGN KEY (BusinessUnitID)
            REFERENCES dbo.tBusinessUnit(BusinessUnitID) ON DELETE NO ACTION,
        CONSTRAINT FK_tUser_tClient FOREIGN KEY (ClientID)
            REFERENCES dbo.tClient(ClientID) ON DELETE NO ACTION,
        CONSTRAINT FK_tUser_tStatus FOREIGN KEY (StatusID)
            REFERENCES dbo.tStatus(StatusID) ON DELETE NO ACTION
    );
    CREATE UNIQUE INDEX UQ_tUser_Email_Active ON dbo.tUser(Email) WHERE IsDeleted = 0;
    CREATE INDEX IX_tUser_BusinessUnitID ON dbo.tUser(BusinessUnitID);
    CREATE INDEX IX_tUser_ClientID ON dbo.tUser(ClientID);
    CREATE INDEX IX_tUser_StatusID ON dbo.tUser(StatusID);
END;
GO

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
        CONSTRAINT FK_tUserAddress_tUser FOREIGN KEY (UserID)
            REFERENCES dbo.tUser(UserID) ON DELETE CASCADE
    );
    CREATE INDEX IX_tUserAddress_UserID ON dbo.tUserAddress(UserID);
END;
GO

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
GO

IF OBJECT_ID(N'dbo.tUserGroupUser', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tUserGroupUser
    (
        UGID INT NOT NULL,
        UserID INT NOT NULL,
        UserRoleID INT NULL,
        JoinedDate DATETIME2(0) NOT NULL CONSTRAINT DF_tUserGroupUser_JoinedDate DEFAULT (SYSUTCDATETIME()),
        StatusID INT NOT NULL,
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
GO

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
        OwnerUserID INT NOT NULL,
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
            REFERENCES dbo.tStatus(StatusID) ON DELETE NO ACTION,
        CONSTRAINT FK_tProject_tUser_Owner FOREIGN KEY (OwnerUserID)
            REFERENCES dbo.tUser(UserID) ON DELETE NO ACTION
    );
    CREATE UNIQUE INDEX UQ_tProject_ProjectNumber_Active ON dbo.tProject(ProjectNumber) WHERE IsDeleted = 0;
    CREATE INDEX IX_tProject_ClientID ON dbo.tProject(ClientID);
    CREATE INDEX IX_tProject_UGID ON dbo.tProject(UGID);
    CREATE INDEX IX_tProject_ProjectTypeID ON dbo.tProject(ProjectTypeID);
    CREATE INDEX IX_tProject_StatusID ON dbo.tProject(StatusID);
    CREATE INDEX IX_tProject_OwnerUserID ON dbo.tProject(OwnerUserID);
    CREATE INDEX IX_tProject_DueDate ON dbo.tProject(DueDate);
END;
GO

IF OBJECT_ID(N'dbo.tService', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tService
    (
        ServiceID INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tService PRIMARY KEY,
        ServiceName VARCHAR(150) NOT NULL,
        ServiceDescription VARCHAR(255) NULL,
        ServiceCategoryID INT NOT NULL,
        StatusID INT NOT NULL,
        CreatedDate DATETIME2(0) NOT NULL CONSTRAINT DF_tService_CreatedDate DEFAULT (SYSUTCDATETIME()),
        ModifiedDate DATETIME2(0) NULL,
        IsDeleted BIT NOT NULL CONSTRAINT DF_tService_IsDeleted DEFAULT (0),
        DeletedDate DATETIME2(0) NULL,
        CONSTRAINT FK_tService_tServiceCategory FOREIGN KEY (ServiceCategoryID)
            REFERENCES dbo.tServiceCategory(ServiceCategoryID) ON DELETE NO ACTION,
        CONSTRAINT FK_tService_tStatus FOREIGN KEY (StatusID)
            REFERENCES dbo.tStatus(StatusID) ON DELETE NO ACTION
    );
    CREATE UNIQUE INDEX UQ_tService_ServiceName_Active ON dbo.tService(ServiceName) WHERE IsDeleted = 0;
    CREATE INDEX IX_tService_ServiceCategoryID ON dbo.tService(ServiceCategoryID);
    CREATE INDEX IX_tService_StatusID ON dbo.tService(StatusID);
END;
GO

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
        CONSTRAINT PK_tProjectService PRIMARY KEY (ProjectID, ServiceID),
        CONSTRAINT FK_tProjectService_tProject FOREIGN KEY (ProjectID)
            REFERENCES dbo.tProject(ProjectID) ON DELETE CASCADE,
        CONSTRAINT FK_tProjectService_tService FOREIGN KEY (ServiceID)
            REFERENCES dbo.tService(ServiceID) ON DELETE NO ACTION
    );
    CREATE INDEX IX_tProjectService_ServiceID ON dbo.tProjectService(ServiceID);
END;
GO

IF OBJECT_ID(N'dbo.tServiceTag', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tServiceTag
    (
        ServiceID INT NOT NULL,
        TagID INT NOT NULL,
        CONSTRAINT PK_tServiceTag PRIMARY KEY (ServiceID, TagID),
        CONSTRAINT FK_tServiceTag_tService FOREIGN KEY (ServiceID)
            REFERENCES dbo.tService(ServiceID) ON DELETE CASCADE,
        CONSTRAINT FK_tServiceTag_tTag FOREIGN KEY (TagID)
            REFERENCES dbo.tTag(TagID) ON DELETE NO ACTION
    );
    CREATE INDEX IX_tServiceTag_TagID ON dbo.tServiceTag(TagID);
END;
GO

IF OBJECT_ID(N'dbo.tBusinessClassTag', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tBusinessClassTag
    (
        BusinessClassID INT NOT NULL,
        TagID INT NOT NULL,
        CONSTRAINT PK_tBusinessClassTag PRIMARY KEY (BusinessClassID, TagID),
        CONSTRAINT FK_tBusinessClassTag_tBusinessClass FOREIGN KEY (BusinessClassID)
            REFERENCES dbo.tBusinessClass(BusinessClassID) ON DELETE CASCADE,
        CONSTRAINT FK_tBusinessClassTag_tTag FOREIGN KEY (TagID)
            REFERENCES dbo.tTag(TagID) ON DELETE NO ACTION
    );
    CREATE INDEX IX_tBusinessClassTag_TagID ON dbo.tBusinessClassTag(TagID);
END;
GO

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
        CONSTRAINT FK_tProjectRevenue_tProject FOREIGN KEY (ProjectID)
            REFERENCES dbo.tProject(ProjectID) ON DELETE CASCADE,
        CONSTRAINT FK_tProjectRevenue_tUser_RecordedBy FOREIGN KEY (RecordedBy)
            REFERENCES dbo.tUser(UserID) ON DELETE NO ACTION
    );
    CREATE INDEX IX_tProjectRevenue_ProjectID_EffectiveDate ON dbo.tProjectRevenue(ProjectID, EffectiveDate DESC);
    CREATE INDEX IX_tProjectRevenue_RecordedBy ON dbo.tProjectRevenue(RecordedBy);
END;
GO

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
        CONSTRAINT FK_tProjectNote_tProject FOREIGN KEY (ProjectID)
            REFERENCES dbo.tProject(ProjectID) ON DELETE CASCADE,
        CONSTRAINT FK_tProjectNote_tUser FOREIGN KEY (UserID)
            REFERENCES dbo.tUser(UserID) ON DELETE NO ACTION
    );
    CREATE INDEX IX_tProjectNote_ProjectID_NoteDate ON dbo.tProjectNote(ProjectID, IsPinned DESC, NoteDate DESC);
    CREATE INDEX IX_tProjectNote_UserID ON dbo.tProjectNote(UserID);
END;
GO

IF OBJECT_ID(N'dbo.tActivityLog', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tActivityLog
    (
        LogID BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tActivityLog PRIMARY KEY,
        EntityType VARCHAR(50) NOT NULL,
        EntityID INT NOT NULL,
        UserID INT NULL,
        ActionDate DATETIME2(0) NOT NULL CONSTRAINT DF_tActivityLog_ActionDate DEFAULT (SYSUTCDATETIME()),
        Action VARCHAR(30) NOT NULL,
        FieldName VARCHAR(100) NULL,
        OldValue NVARCHAR(MAX) NULL,
        NewValue NVARCHAR(MAX) NULL,
        CONSTRAINT FK_tActivityLog_tUser FOREIGN KEY (UserID)
            REFERENCES dbo.tUser(UserID) ON DELETE NO ACTION
    );
    CREATE INDEX IX_tActivityLog_Entity ON dbo.tActivityLog(EntityType, EntityID, ActionDate DESC);
    CREATE INDEX IX_tActivityLog_UserID_ActionDate ON dbo.tActivityLog(UserID, ActionDate DESC);
END;
GO
