--tBusinessUnit
CREATE OR ALTER PROCEDURE usp_CreateTable_tBusinessUnit
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID('dbo.tBusinessUnit', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.tBusinessUnit (
            BusinessUnitID INT IDENTITY(1,1) NOT NULL,
        BusinessUnitName             VARCHAR(100)         NOT NULL,
        BusinessUnitDescription      VARCHAR(255)         NULL,
        IsActive                     BIT                  NOT NULL CONSTRAINT DF_tBusinessUnit_IsActive DEFAULT (1),
        CONSTRAINT PK_tBusinessUnit PRIMARY KEY CLUSTERED (BusinessUnitID),
        CONSTRAINT UQ_tBusinessUnit_BusinessUnitName UNIQUE (BusinessUnitName)
        );
    END;

    -- Seed rows (idempotent)
    SET IDENTITY_INSERT dbo.tBusinessUnit ON;

    MERGE dbo.tBusinessUnit AS tgt
    USING (VALUES
        (1, 'US', 'U.S. Clients',          1),
        (2, 'UK', 'U.K. Clients',          1),
        (3, 'LA', 'Latin America Clients', 1),
        (4, 'Global', 'Global Clients',    1)
    ) AS src (BusinessUnitID, BusinessUnitName, BusinessUnitDescription, IsActive)
      ON tgt.BusinessUnitID = src.BusinessUnitID
    WHEN NOT MATCHED BY TARGET THEN
        INSERT (BusinessUnitID, BusinessUnitName, BusinessUnitDescription, IsActive)
        VALUES (src.BusinessUnitID, src.BusinessUnitName, src.BusinessUnitDescription, src.IsActive);

    SET IDENTITY_INSERT dbo.tBusinessUnit OFF;
END;
GO
CREATE OR ALTER PROCEDURE usp_BusinessUnit_Create
    @BusinessUnitName             VARCHAR(100),
    @BusinessUnitDescription      VARCHAR(255) = NULL,
    @ActingUserID                INT,
    @NewBusinessUnitID           INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRAN;

        INSERT INTO dbo.tBusinessUnit (BusinessUnitName, BusinessUnitDescription)
        VALUES (@BusinessUnitName, @BusinessUnitDescription);

        SET @NewBusinessUnitID = SCOPE_IDENTITY();

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'BusinessUnit',
            @EntityID   = @NewBusinessUnitID,
            @UserID     = @ActingUserID,
            @Action     = 'Insert';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE usp_BusinessUnit_Get
    @BusinessUnitID INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT * FROM dbo.tBusinessUnit WHERE BusinessUnitID = @BusinessUnitID;
END;
GO
CREATE OR ALTER PROCEDURE usp_BusinessUnit_List
    @ActiveOnly BIT = 1
AS
BEGIN
    SET NOCOUNT ON;
    SELECT *
      FROM dbo.tBusinessUnit
     WHERE (@ActiveOnly = 0 OR IsActive = 1);
END;
GO
CREATE OR ALTER PROCEDURE usp_BusinessUnit_Update
    @BusinessUnitID               INT,
    @BusinessUnitName             VARCHAR(100) = NULL,
    @BusinessUnitDescription      VARCHAR(255) = NULL,
    @ActingUserID                INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.tBusinessUnit WHERE BusinessUnitID = @BusinessUnitID)
        THROW 50001, 'tBusinessUnit not found.', 1;

    BEGIN TRY
        BEGIN TRAN;

        UPDATE dbo.tBusinessUnit
           SET
        BusinessUnitName = COALESCE(@BusinessUnitName, BusinessUnitName),
        BusinessUnitDescription = COALESCE(@BusinessUnitDescription, BusinessUnitDescription)
         WHERE BusinessUnitID = @BusinessUnitID;

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'BusinessUnit',
            @EntityID   = @BusinessUnitID,
            @UserID     = @ActingUserID,
            @Action     = 'Update';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE usp_BusinessUnit_SoftDelete
    @BusinessUnitID INT,
    @ActingUserID INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.tBusinessUnit WHERE BusinessUnitID = @BusinessUnitID AND IsActive = 1)
        THROW 50001, 'tBusinessUnit not found or already inactive.', 1;

    BEGIN TRY
        BEGIN TRAN;

        UPDATE dbo.tBusinessUnit
           SET IsActive = 0
         WHERE BusinessUnitID = @BusinessUnitID;

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'BusinessUnit',
            @EntityID   = @BusinessUnitID,
            @UserID     = @ActingUserID,
            @Action     = 'SoftDelete';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE usp_BusinessUnit_HardDelete
    @BusinessUnitID INT,
    @ActingUserID   INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.tBusinessUnit WHERE BusinessUnitID = @BusinessUnitID)
        THROW 50001, 'tBusinessUnit not found.', 1;

    DECLARE @ActiveProjectCount INT =
    (
        SELECT COUNT(DISTINCT p.ProjectID)
          FROM dbo.tProject AS p
         INNER JOIN dbo.tProjectRef AS pr
            ON pr.ProjectID = p.ProjectID
           AND pr.IsActive = 1
         INNER JOIN dbo.tClientRef AS cr
            ON cr.ClientRefID = pr.ClientRefID
         WHERE cr.BusinessUnitID = @BusinessUnitID
           AND p.IsActive = 1
    );

    IF @ActiveProjectCount > 0
        THROW 50002, 'This business unit cannot be deleted because it is associated with active projects. Please remove the project before deleting the business unit.', 1;

    DECLARE @BusinessUnitName VARCHAR(100);
    SELECT @BusinessUnitName = BusinessUnitName
      FROM dbo.tBusinessUnit
     WHERE BusinessUnitID = @BusinessUnitID;

    BEGIN TRY
        BEGIN TRAN;

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'BusinessUnit',
            @EntityID   = @BusinessUnitID,
            @UserID     = @ActingUserID,
            @Action     = 'Remove',
            @FieldName  = NULL,
            @OldValue   = @BusinessUnitName,
            @NewValue   = NULL;

        UPDATE dbo.tClientRef
           SET BusinessUnitID = NULL
         WHERE BusinessUnitID = @BusinessUnitID;

        IF COL_LENGTH(N'dbo.tBusinessClass', N'BusinessUnitID') IS NOT NULL
        BEGIN
            DECLARE @clearBcSql nvarchar(400) =
                N'UPDATE dbo.tBusinessClass SET BusinessUnitID = NULL WHERE BusinessUnitID = @BusinessUnitID';
            EXEC sys.sp_executesql
                @clearBcSql,
                N'@BusinessUnitID INT',
                @BusinessUnitID = @BusinessUnitID;
        END;

        DELETE FROM dbo.tBusinessUnit
         WHERE BusinessUnitID = @BusinessUnitID;

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO

--tBusinessClass
CREATE OR ALTER PROCEDURE usp_CreateTable_tBusinessClass
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID('dbo.tBusinessClass', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.tBusinessClass (
            BusinessClassID INT IDENTITY(1,1) NOT NULL,
        BusinessClassName            VARCHAR(100)         NOT NULL,
        BusinessClassDescription     VARCHAR(255)         NULL,
        IsActive                     BIT                  NOT NULL CONSTRAINT DF_tBusinessClass_IsActive DEFAULT (1),
        CONSTRAINT PK_tBusinessClass PRIMARY KEY CLUSTERED (BusinessClassID),
        CONSTRAINT UQ_tBusinessClass_BusinessClassName UNIQUE (BusinessClassName)
        );
    END;

    -- Seed rows (idempotent): BusinessClassName = short code, BusinessClassDescription = full name.
    MERGE dbo.tBusinessClass AS tgt
    USING (VALUES
        ('Re',     'Reinsurance'),
        ('NM Re',  'Non-Marine Retro'),
        ('NA DF',  'North American D&F'),
        ('Int DF', 'International D&F'),
        ('DUA',    'Delegated Authority'),
        ('EN',     'Energy'),
        ('MA',     'Marine'),
        ('SP Re',  'Specialty Re'),
        ('NZ',     'BMS Risk NZ'),
        ('BE',     'Bermuda'),
        ('TR',     'Turkey'),
        ('PS',     'Portfolio Solutions')
    ) AS src (BusinessClassName, BusinessClassDescription)
      ON tgt.BusinessClassName = src.BusinessClassName
    WHEN NOT MATCHED BY TARGET THEN
        INSERT (BusinessClassName, BusinessClassDescription)
        VALUES (src.BusinessClassName, src.BusinessClassDescription)
    WHEN MATCHED THEN
        UPDATE SET BusinessClassDescription = src.BusinessClassDescription;
END;
GO
CREATE OR ALTER PROCEDURE usp_BusinessClass_Create
    @BusinessClassName            VARCHAR(100),
    @BusinessClassDescription     VARCHAR(255) = NULL,
    @ActingUserID                INT,
    @NewBusinessClassID          INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRAN;

        INSERT INTO dbo.tBusinessClass (BusinessClassName, BusinessClassDescription)
        VALUES (@BusinessClassName, @BusinessClassDescription);

        SET @NewBusinessClassID = SCOPE_IDENTITY();

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'BusinessClass',
            @EntityID   = @NewBusinessClassID,
            @UserID     = @ActingUserID,
            @Action     = 'Insert';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE usp_BusinessClass_Get
    @BusinessClassID INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT * FROM dbo.tBusinessClass WHERE BusinessClassID = @BusinessClassID;
END;
GO
CREATE OR ALTER PROCEDURE usp_BusinessClass_List
    @ActiveOnly BIT = 1
AS
BEGIN
    SET NOCOUNT ON;
    SELECT *
      FROM dbo.tBusinessClass
     WHERE (@ActiveOnly = 0 OR IsActive = 1);
END;
GO
CREATE OR ALTER PROCEDURE usp_BusinessClass_Update
    @BusinessClassID              INT,
    @BusinessClassName            VARCHAR(100) = NULL,
    @BusinessClassDescription     VARCHAR(255) = NULL,
    @ActingUserID                INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.tBusinessClass WHERE BusinessClassID = @BusinessClassID)
        THROW 50001, 'tBusinessClass not found.', 1;

    BEGIN TRY
        BEGIN TRAN;

        UPDATE dbo.tBusinessClass
           SET
        BusinessClassName = COALESCE(@BusinessClassName, BusinessClassName),
        BusinessClassDescription = COALESCE(@BusinessClassDescription, BusinessClassDescription)
         WHERE BusinessClassID = @BusinessClassID;

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'BusinessClass',
            @EntityID   = @BusinessClassID,
            @UserID     = @ActingUserID,
            @Action     = 'Update';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE usp_BusinessClass_SoftDelete
    @BusinessClassID INT,
    @ActingUserID INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.tBusinessClass WHERE BusinessClassID = @BusinessClassID AND IsActive = 1)
        THROW 50001, 'tBusinessClass not found or already inactive.', 1;

    BEGIN TRY
        BEGIN TRAN;

        UPDATE dbo.tBusinessClass
           SET IsActive = 0
         WHERE BusinessClassID = @BusinessClassID;

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'BusinessClass',
            @EntityID   = @BusinessClassID,
            @UserID     = @ActingUserID,
            @Action     = 'SoftDelete';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE usp_BusinessClass_HardDelete
    @BusinessClassID INT,
    @ActingUserID    INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.tBusinessClass WHERE BusinessClassID = @BusinessClassID)
        THROW 50001, 'tBusinessClass not found.', 1;

    DECLARE @ActiveProjectCount INT =
    (
        SELECT COUNT(DISTINCT p.ProjectID)
          FROM dbo.tProject AS p
         INNER JOIN dbo.tProjectRef AS pr
            ON pr.ProjectID = p.ProjectID
           AND pr.IsActive = 1
         INNER JOIN dbo.tClientRef AS cr
            ON cr.ClientRefID = pr.ClientRefID
         WHERE cr.BusinessClassID = @BusinessClassID
           AND p.IsActive = 1
    );

    IF @ActiveProjectCount > 0
        THROW 50002, 'This business class cannot be deleted because it is associated with active projects. Please remove the project before deleting the business class.', 1;

    DECLARE @BusinessClassName VARCHAR(100);
    SELECT @BusinessClassName = BusinessClassName
      FROM dbo.tBusinessClass
     WHERE BusinessClassID = @BusinessClassID;

    BEGIN TRY
        BEGIN TRAN;

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'BusinessClass',
            @EntityID   = @BusinessClassID,
            @UserID     = @ActingUserID,
            @Action     = 'Remove',
            @FieldName  = NULL,
            @OldValue   = @BusinessClassName,
            @NewValue   = NULL;

        UPDATE dbo.tClientRef
           SET BusinessClassID = NULL
         WHERE BusinessClassID = @BusinessClassID;

        DELETE FROM dbo.tBusinessClass
         WHERE BusinessClassID = @BusinessClassID;

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO

--tClientType
CREATE OR ALTER PROCEDURE usp_CreateTable_tClientType
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID('dbo.tClientType', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.tClientType (
            ClientTypeID INT IDENTITY(1,1) NOT NULL,
        ClientTypeName               VARCHAR(100)         NOT NULL,
        ClientTypeDescription        VARCHAR(255)         NULL,
        IsActive                     BIT                  NOT NULL CONSTRAINT DF_tClientType_IsActive DEFAULT (1),
        CONSTRAINT PK_tClientType PRIMARY KEY CLUSTERED (ClientTypeID),
        CONSTRAINT UQ_tClientType_ClientTypeName UNIQUE (ClientTypeName)
        );
    END;

    -- Seed rows (idempotent): ClientTypeName = short code, ClientTypeDescription = full name.
    SET IDENTITY_INSERT dbo.tClientType ON;

    MERGE dbo.tClientType AS tgt
    USING (VALUES
        (1, N'PIC', N'Primary Insurance Carrier'),
        (2, N'MGA', N'Managing General Agent'),
        (3, N'FC',  N'Fronting Carriers'),
        (4, N'RRG', N'Risk Retention Groups'),
        (5, N'RM',  N'Residual Market - Govt')
    ) AS src (ClientTypeID, ClientTypeName, ClientTypeDescription)
      ON tgt.ClientTypeID = src.ClientTypeID
    WHEN NOT MATCHED BY TARGET THEN
        INSERT (ClientTypeID, ClientTypeName, ClientTypeDescription)
        VALUES (src.ClientTypeID, src.ClientTypeName, src.ClientTypeDescription)
    WHEN MATCHED THEN
        UPDATE SET ClientTypeName = src.ClientTypeName,
                   ClientTypeDescription = src.ClientTypeDescription;

    SET IDENTITY_INSERT dbo.tClientType OFF;
END;
GO
CREATE OR ALTER PROCEDURE usp_ClientType_Create
    @ClientTypeName               VARCHAR(100),
    @ClientTypeDescription        VARCHAR(255) = NULL,
    @ActingUserID                INT,
    @NewClientTypeID             INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRAN;

        INSERT INTO dbo.tClientType (ClientTypeName, ClientTypeDescription)
        VALUES (@ClientTypeName, @ClientTypeDescription);

        SET @NewClientTypeID = SCOPE_IDENTITY();

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'ClientType',
            @EntityID   = @NewClientTypeID,
            @UserID     = @ActingUserID,
            @Action     = 'Insert';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE usp_ClientType_Get
    @ClientTypeID INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT * FROM dbo.tClientType WHERE ClientTypeID = @ClientTypeID;
END;
GO
CREATE OR ALTER PROCEDURE usp_ClientType_List
    @ActiveOnly BIT = 1
AS
BEGIN
    SET NOCOUNT ON;
    SELECT *
      FROM dbo.tClientType
     WHERE (@ActiveOnly = 0 OR IsActive = 1);
END;
GO
CREATE OR ALTER PROCEDURE usp_ClientType_Update
    @ClientTypeID                 INT,
    @ClientTypeName               VARCHAR(100) = NULL,
    @ClientTypeDescription        VARCHAR(255) = NULL,
    @ActingUserID                INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.tClientType WHERE ClientTypeID = @ClientTypeID)
        THROW 50001, 'tClientType not found.', 1;

    BEGIN TRY
        BEGIN TRAN;

        UPDATE dbo.tClientType
           SET
        ClientTypeName = COALESCE(@ClientTypeName, ClientTypeName),
        ClientTypeDescription = COALESCE(@ClientTypeDescription, ClientTypeDescription)
         WHERE ClientTypeID = @ClientTypeID;

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'ClientType',
            @EntityID   = @ClientTypeID,
            @UserID     = @ActingUserID,
            @Action     = 'Update';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE usp_ClientType_SoftDelete
    @ClientTypeID INT,
    @ActingUserID INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.tClientType WHERE ClientTypeID = @ClientTypeID AND IsActive = 1)
        THROW 50001, 'tClientType not found or already inactive.', 1;

    BEGIN TRY
        BEGIN TRAN;

        UPDATE dbo.tClientType
           SET IsActive = 0
         WHERE ClientTypeID = @ClientTypeID;

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'ClientType',
            @EntityID   = @ClientTypeID,
            @UserID     = @ActingUserID,
            @Action     = 'SoftDelete';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO

--tClientTypeRef
CREATE OR ALTER PROCEDURE usp_CreateTable_tClientTypeRef
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID('dbo.tClientTypeRef', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.tClientTypeRef (
            ClientTypeRefID INT IDENTITY(1,1) NOT NULL,
        ClientTypeRefName            VARCHAR(100)         NOT NULL,
        IsActive                     BIT                  NOT NULL CONSTRAINT DF_tClientTypeRef_IsActive DEFAULT (1),
        CONSTRAINT PK_tClientTypeRef PRIMARY KEY CLUSTERED (ClientTypeRefID),
        CONSTRAINT UQ_tClientTypeRef_ClientTypeRefName UNIQUE (ClientTypeRefName)
        );
    END;
END;
GO
CREATE OR ALTER PROCEDURE usp_ClientTypeRef_Create
    @ClientTypeRefName            VARCHAR(100),
    @ActingUserID                INT,
    @NewClientTypeRefID          INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRAN;

        INSERT INTO dbo.tClientTypeRef (ClientTypeRefName)
        VALUES (@ClientTypeRefName);

        SET @NewClientTypeRefID = SCOPE_IDENTITY();

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'ClientTypeRef',
            @EntityID   = @NewClientTypeRefID,
            @UserID     = @ActingUserID,
            @Action     = 'Insert';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE usp_ClientTypeRef_Get
    @ClientTypeRefID INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT * FROM dbo.tClientTypeRef WHERE ClientTypeRefID = @ClientTypeRefID;
END;
GO
CREATE OR ALTER PROCEDURE usp_ClientTypeRef_List
    @ActiveOnly BIT = 1
AS
BEGIN
    SET NOCOUNT ON;
    SELECT *
      FROM dbo.tClientTypeRef
     WHERE (@ActiveOnly = 0 OR IsActive = 1);
END;
GO
CREATE OR ALTER PROCEDURE usp_ClientTypeRef_Update
    @ClientTypeRefID              INT,
    @ClientTypeRefName            VARCHAR(100) = NULL,
    @ActingUserID                INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.tClientTypeRef WHERE ClientTypeRefID = @ClientTypeRefID)
        THROW 50001, 'tClientTypeRef not found.', 1;

    BEGIN TRY
        BEGIN TRAN;

        UPDATE dbo.tClientTypeRef
           SET
        ClientTypeRefName = COALESCE(@ClientTypeRefName, ClientTypeRefName)
         WHERE ClientTypeRefID = @ClientTypeRefID;

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'ClientTypeRef',
            @EntityID   = @ClientTypeRefID,
            @UserID     = @ActingUserID,
            @Action     = 'Update';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE usp_ClientTypeRef_SoftDelete
    @ClientTypeRefID INT,
    @ActingUserID INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.tClientTypeRef WHERE ClientTypeRefID = @ClientTypeRefID AND IsActive = 1)
        THROW 50001, 'tClientTypeRef not found or already inactive.', 1;

    BEGIN TRY
        BEGIN TRAN;

        UPDATE dbo.tClientTypeRef
           SET IsActive = 0
         WHERE ClientTypeRefID = @ClientTypeRefID;

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'ClientTypeRef',
            @EntityID   = @ClientTypeRefID,
            @UserID     = @ActingUserID,
            @Action     = 'SoftDelete';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO

--tTag
CREATE OR ALTER PROCEDURE usp_CreateTable_tTag
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID('dbo.tTag', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.tTag (
            TagID INT IDENTITY(1,1) NOT NULL,
        TagName                      VARCHAR(50)          NOT NULL,
        TagScope                     VARCHAR(30)          NOT NULL,
        IsActive                     BIT                  NOT NULL CONSTRAINT DF_tTag_IsActive DEFAULT (1),
        CONSTRAINT PK_tTag PRIMARY KEY CLUSTERED (TagID),
        CONSTRAINT UQ_tTag_TagName UNIQUE (TagName)
        );
    END;
END;
GO
CREATE OR ALTER PROCEDURE usp_Tag_Create
    @TagName                      VARCHAR(50),
    @TagScope                     VARCHAR(30),
    @ActingUserID                INT,
    @NewTagID                    INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRAN;

        INSERT INTO dbo.tTag (TagName, TagScope)
        VALUES (@TagName, @TagScope);

        SET @NewTagID = SCOPE_IDENTITY();

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'Tag',
            @EntityID   = @NewTagID,
            @UserID     = @ActingUserID,
            @Action     = 'Insert';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE usp_Tag_Get
    @TagID INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT * FROM dbo.tTag WHERE TagID = @TagID;
END;
GO
CREATE OR ALTER PROCEDURE usp_Tag_List
    @ActiveOnly BIT = 1
AS
BEGIN
    SET NOCOUNT ON;
    SELECT *
      FROM dbo.tTag
     WHERE (@ActiveOnly = 0 OR IsActive = 1);
END;
GO
CREATE OR ALTER PROCEDURE usp_Tag_Update
    @TagID                        INT,
    @TagName                      VARCHAR(50) = NULL,
    @TagScope                     VARCHAR(30) = NULL,
    @ActingUserID                INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.tTag WHERE TagID = @TagID)
        THROW 50001, 'tTag not found.', 1;

    BEGIN TRY
        BEGIN TRAN;

        UPDATE dbo.tTag
           SET
        TagName = COALESCE(@TagName, TagName),
        TagScope = COALESCE(@TagScope, TagScope)
         WHERE TagID = @TagID;

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'Tag',
            @EntityID   = @TagID,
            @UserID     = @ActingUserID,
            @Action     = 'Update';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE usp_Tag_SoftDelete
    @TagID INT,
    @ActingUserID INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.tTag WHERE TagID = @TagID AND IsActive = 1)
        THROW 50001, 'tTag not found or already inactive.', 1;

    BEGIN TRY
        BEGIN TRAN;

        UPDATE dbo.tTag
           SET IsActive = 0
         WHERE TagID = @TagID;

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'Tag',
            @EntityID   = @TagID,
            @UserID     = @ActingUserID,
            @Action     = 'SoftDelete';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO

--tProjectType
CREATE OR ALTER PROCEDURE usp_CreateTable_tProjectType
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID('dbo.tProjectType', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.tProjectType (
            ProjectTypeID INT IDENTITY(1,1) NOT NULL,
        ProjectTypeCode              VARCHAR(20)          NOT NULL,
        ProjectTypeDescription       VARCHAR(255)         NULL,
        IsActive                     BIT                  NOT NULL CONSTRAINT DF_tProjectType_IsActive DEFAULT (1),
        CONSTRAINT PK_tProjectType PRIMARY KEY CLUSTERED (ProjectTypeID),
        CONSTRAINT UQ_tProjectType_ProjectTypeCode UNIQUE (ProjectTypeCode)
        );
    END;

    -- Seed rows (idempotent)
    SET IDENTITY_INSERT dbo.tProjectType ON;

    MERGE dbo.tProjectType AS tgt
    USING (VALUES
        (1, 'RNWL',  'Renewal',        1),
        (2, 'NB',    'New Business',   1),
        (3, 'Pro',   'Proforma',       1),
        (4, 'AdHoc', 'AdHoc Analysis', 1)
    ) AS src (ProjectTypeID, ProjectTypeCode, ProjectTypeDescription, IsActive)
      ON tgt.ProjectTypeID = src.ProjectTypeID
    WHEN NOT MATCHED BY TARGET THEN
        INSERT (ProjectTypeID, ProjectTypeCode, ProjectTypeDescription, IsActive)
        VALUES (src.ProjectTypeID, src.ProjectTypeCode, src.ProjectTypeDescription, src.IsActive);

    SET IDENTITY_INSERT dbo.tProjectType OFF;
END;
GO
CREATE OR ALTER PROCEDURE usp_ProjectType_Create
    @ProjectTypeCode              VARCHAR(20),
    @ProjectTypeDescription       VARCHAR(255) = NULL,
    @ActingUserID                INT,
    @NewProjectTypeID            INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRAN;

        INSERT INTO dbo.tProjectType (ProjectTypeCode, ProjectTypeDescription)
        VALUES (@ProjectTypeCode, @ProjectTypeDescription);

        SET @NewProjectTypeID = SCOPE_IDENTITY();

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'ProjectType',
            @EntityID   = @NewProjectTypeID,
            @UserID     = @ActingUserID,
            @Action     = 'Insert';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE usp_ProjectType_Get
    @ProjectTypeID INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT * FROM dbo.tProjectType WHERE ProjectTypeID = @ProjectTypeID;
END;
GO
CREATE OR ALTER PROCEDURE usp_ProjectType_List
    @ActiveOnly BIT = 1
AS
BEGIN
    SET NOCOUNT ON;
    SELECT *
      FROM dbo.tProjectType
     WHERE (@ActiveOnly = 0 OR IsActive = 1);
END;
GO
CREATE OR ALTER PROCEDURE usp_ProjectType_Update
    @ProjectTypeID                INT,
    @ProjectTypeCode              VARCHAR(20) = NULL,
    @ProjectTypeDescription       VARCHAR(255) = NULL,
    @ActingUserID                INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.tProjectType WHERE ProjectTypeID = @ProjectTypeID)
        THROW 50001, 'tProjectType not found.', 1;

    BEGIN TRY
        BEGIN TRAN;

        UPDATE dbo.tProjectType
           SET
        ProjectTypeCode = COALESCE(@ProjectTypeCode, ProjectTypeCode),
        ProjectTypeDescription = COALESCE(@ProjectTypeDescription, ProjectTypeDescription)
         WHERE ProjectTypeID = @ProjectTypeID;

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'ProjectType',
            @EntityID   = @ProjectTypeID,
            @UserID     = @ActingUserID,
            @Action     = 'Update';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE usp_ProjectType_SoftDelete
    @ProjectTypeID INT,
    @ActingUserID INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.tProjectType WHERE ProjectTypeID = @ProjectTypeID AND IsActive = 1)
        THROW 50001, 'tProjectType not found or already inactive.', 1;

    BEGIN TRY
        BEGIN TRAN;

        UPDATE dbo.tProjectType
           SET IsActive = 0
         WHERE ProjectTypeID = @ProjectTypeID;

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'ProjectType',
            @EntityID   = @ProjectTypeID,
            @UserID     = @ActingUserID,
            @Action     = 'SoftDelete';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO

--tUserRole
CREATE OR ALTER PROCEDURE usp_CreateTable_tUserRole
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID('dbo.tUserRole', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.tUserRole (
            UserRoleID INT IDENTITY(1,1) NOT NULL,
        UserRoleName                 VARCHAR(100)         NOT NULL,
        RoleDescription              VARCHAR(255)         NULL,
        IsActive                     BIT                  NOT NULL CONSTRAINT DF_tUserRole_IsActive DEFAULT (1),
        CONSTRAINT PK_tUserRole PRIMARY KEY CLUSTERED (UserRoleID),
        CONSTRAINT UQ_tUserRole_UserRoleName UNIQUE (UserRoleName)
        );
    END;
END;
GO
CREATE OR ALTER PROCEDURE usp_UserRole_Create
    @UserRoleName                 VARCHAR(100),
    @RoleDescription              VARCHAR(255) = NULL,
    @IsActive                     BIT = 1,
    @ActingUserID                INT,
    @NewUserRoleID               INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRAN;

        INSERT INTO dbo.tUserRole (UserRoleName, RoleDescription, IsActive)
        VALUES (@UserRoleName, @RoleDescription, COALESCE(@IsActive, 1));

        SET @NewUserRoleID = SCOPE_IDENTITY();

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'UserRole',
            @EntityID   = @NewUserRoleID,
            @UserID     = @ActingUserID,
            @Action     = 'Insert';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE usp_UserRole_Get
    @UserRoleID INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT * FROM dbo.tUserRole WHERE UserRoleID = @UserRoleID;
END;
GO
CREATE OR ALTER PROCEDURE usp_UserRole_List
    @ActiveOnly BIT = 1
AS
BEGIN
    SET NOCOUNT ON;
    SELECT *
      FROM dbo.tUserRole
     WHERE (@ActiveOnly = 0 OR IsActive = 1);
END;
GO
CREATE OR ALTER PROCEDURE usp_UserRole_Update
    @UserRoleID                   INT,
    @UserRoleName                 VARCHAR(100) = NULL,
    @RoleDescription              VARCHAR(255) = NULL,
    @IsActive                     BIT = NULL,
    @ActingUserID                INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.tUserRole WHERE UserRoleID = @UserRoleID)
        THROW 50001, 'tUserRole not found.', 1;

    BEGIN TRY
        BEGIN TRAN;

        UPDATE dbo.tUserRole
           SET
        UserRoleName = COALESCE(@UserRoleName, UserRoleName),
        RoleDescription = @RoleDescription,
        IsActive = COALESCE(@IsActive, IsActive)
         WHERE UserRoleID = @UserRoleID;

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'UserRole',
            @EntityID   = @UserRoleID,
            @UserID     = @ActingUserID,
            @Action     = 'Update';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE usp_UserRole_SoftDelete
    @UserRoleID INT,
    @ActingUserID INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.tUserRole WHERE UserRoleID = @UserRoleID AND IsActive = 1)
        THROW 50001, 'tUserRole not found or already inactive.', 1;

    BEGIN TRY
        BEGIN TRAN;

        UPDATE dbo.tUserRole
           SET IsActive = 0
         WHERE UserRoleID = @UserRoleID;

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'UserRole',
            @EntityID   = @UserRoleID,
            @UserID     = @ActingUserID,
            @Action     = 'SoftDelete';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO

--tUserGroup
CREATE OR ALTER PROCEDURE usp_CreateTable_tUserGroup
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID('dbo.tUserGroup', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.tUserGroup (
            UserGroupID INT IDENTITY(1,1) NOT NULL,
        UserGroupNumber              VARCHAR(50)          NOT NULL,
        UserGroupName                VARCHAR(100)         NOT NULL,
        IsActive                     BIT                  NOT NULL CONSTRAINT DF_tUserGroup_IsActive DEFAULT (1),
        CONSTRAINT PK_tUserGroup PRIMARY KEY CLUSTERED (UserGroupID),
        CONSTRAINT UQ_tUserGroup_UserGroupName UNIQUE (UserGroupName)
        );
    END;
END;
GO
CREATE OR ALTER PROCEDURE usp_UserGroup_Create
    @UserGroupNumber              VARCHAR(50),
    @UserGroupName                VARCHAR(100),
    @ActingUserID                INT,
    @NewUserGroupID              INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRAN;

        INSERT INTO dbo.tUserGroup (UserGroupNumber, UserGroupName)
        VALUES (@UserGroupNumber, @UserGroupName);

        SET @NewUserGroupID = SCOPE_IDENTITY();

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'UserGroup',
            @EntityID   = @NewUserGroupID,
            @UserID     = @ActingUserID,
            @Action     = 'Insert';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE usp_UserGroup_GetNextNumber
AS
BEGIN
    SET NOCOUNT ON;

    SELECT CAST(ISNULL(MAX(TRY_CAST(UserGroupNumber AS INT)), 0) + 1 AS VARCHAR(50)) AS NextUserGroupNumber
      FROM dbo.tUserGroup;
END;
GO
CREATE OR ALTER PROCEDURE usp_UserGroup_Get
    @UserGroupID INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT * FROM dbo.tUserGroup WHERE UserGroupID = @UserGroupID;
END;
GO
CREATE OR ALTER PROCEDURE usp_UserGroup_List
    @ActiveOnly BIT = 1
AS
BEGIN
    SET NOCOUNT ON;
    SELECT *
      FROM dbo.tUserGroup
     WHERE (@ActiveOnly = 0 OR IsActive = 1);
END;
GO
CREATE OR ALTER PROCEDURE usp_UserGroup_Update
    @UserGroupID                  INT,
    @UserGroupNumber              VARCHAR(50) = NULL,
    @UserGroupName                VARCHAR(100) = NULL,
    @ActingUserID                INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.tUserGroup WHERE UserGroupID = @UserGroupID)
        THROW 50001, 'tUserGroup not found.', 1;

    BEGIN TRY
        BEGIN TRAN;

        UPDATE dbo.tUserGroup
           SET
        UserGroupNumber = COALESCE(@UserGroupNumber, UserGroupNumber),
        UserGroupName = COALESCE(@UserGroupName, UserGroupName)
         WHERE UserGroupID = @UserGroupID;

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'UserGroup',
            @EntityID   = @UserGroupID,
            @UserID     = @ActingUserID,
            @Action     = 'Update';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE usp_UserGroup_SoftDelete
    @UserGroupID INT,
    @ActingUserID INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.tUserGroup WHERE UserGroupID = @UserGroupID AND IsActive = 1)
        THROW 50001, 'tUserGroup not found or already inactive.', 1;

    BEGIN TRY
        BEGIN TRAN;

        UPDATE dbo.tUserGroup
           SET IsActive = 0
         WHERE UserGroupID = @UserGroupID;

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'UserGroup',
            @EntityID   = @UserGroupID,
            @UserID     = @ActingUserID,
            @Action     = 'SoftDelete';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO

CREATE OR ALTER PROCEDURE usp_UserGroup_HardDelete
    @UserGroupID  INT,
    @ActingUserID INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.tUserGroup WHERE UserGroupID = @UserGroupID)
        THROW 50001, 'tUserGroup not found.', 1;

    DECLARE @ActiveProjectCount INT =
    (
        SELECT COUNT(DISTINCT p.ProjectID)
          FROM dbo.tProject AS p
         INNER JOIN dbo.tProjectRef AS pr
            ON pr.ProjectID = p.ProjectID
           AND pr.IsActive = 1
         WHERE pr.UserGroupID = @UserGroupID
           AND p.IsActive = 1
    );

    IF @ActiveProjectCount > 0
        THROW 50002, 'This user group cannot be deleted because it is associated with active projects. Please remove the project before deleting the user group.', 1;

    DECLARE @UserGroupName VARCHAR(100);
    SELECT @UserGroupName = UserGroupName
      FROM dbo.tUserGroup
     WHERE UserGroupID = @UserGroupID;

    BEGIN TRY
        BEGIN TRAN;

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'UserGroup',
            @EntityID   = @UserGroupID,
            @UserID     = @ActingUserID,
            @Action     = 'Remove',
            @FieldName  = NULL,
            @OldValue   = @UserGroupName,
            @NewValue   = NULL;

        UPDATE dbo.tProjectRef
           SET UserGroupID = NULL
         WHERE UserGroupID = @UserGroupID;

        DELETE FROM dbo.tUserGroupRef
         WHERE UserGroupID = @UserGroupID;

        DELETE FROM dbo.tUserGroup
         WHERE UserGroupID = @UserGroupID;

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO

--tServiceGroup
CREATE OR ALTER PROCEDURE usp_CreateTable_tServiceGroup
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID('dbo.tServiceGroup', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.tServiceGroup (
            ServiceGroupID INT IDENTITY(1,1) NOT NULL,
        ServiceGroupNumber           VARCHAR(50)          NOT NULL,
        ServiceGroupName             VARCHAR(100)         NOT NULL,
        ServiceGroupDescription      VARCHAR(255)         NOT NULL,
        IsActive                     BIT                  NOT NULL CONSTRAINT DF_tServiceGroup_IsActive DEFAULT (1),
        CONSTRAINT PK_tServiceGroup PRIMARY KEY CLUSTERED (ServiceGroupID),
        CONSTRAINT UQ_tServiceGroup_ServiceGroupName UNIQUE (ServiceGroupName)
        );
    END;
END;
GO
CREATE OR ALTER PROCEDURE usp_ServiceGroup_Create
    @ServiceGroupNumber           VARCHAR(50),
    @ServiceGroupName             VARCHAR(100),
    @ActingUserID                INT,
    @NewServiceGroupID           INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRAN;

        INSERT INTO dbo.tServiceGroup (ServiceGroupNumber, ServiceGroupName)
        VALUES (@ServiceGroupNumber, @ServiceGroupName);

        SET @NewServiceGroupID = SCOPE_IDENTITY();

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'ServiceGroup',
            @EntityID   = @NewServiceGroupID,
            @UserID     = @ActingUserID,
            @Action     = 'Insert';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE usp_ServiceGroup_Get
    @ServiceGroupID INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT * FROM dbo.tServiceGroup WHERE ServiceGroupID = @ServiceGroupID;
END;
GO
CREATE OR ALTER PROCEDURE usp_ServiceGroup_List
    @ActiveOnly BIT = 1
AS
BEGIN
    SET NOCOUNT ON;
    SELECT *
      FROM dbo.tServiceGroup
     WHERE (@ActiveOnly = 0 OR IsActive = 1);
END;
GO
CREATE OR ALTER PROCEDURE usp_ServiceGroup_Update
    @ServiceGroupID               INT,
    @ServiceGroupNumber           VARCHAR(50) = NULL,
    @ServiceGroupName             VARCHAR(100) = NULL,
    @ActingUserID                INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.tServiceGroup WHERE ServiceGroupID = @ServiceGroupID)
        THROW 50001, 'tServiceGroup not found.', 1;

    BEGIN TRY
        BEGIN TRAN;

        UPDATE dbo.tServiceGroup
           SET
        ServiceGroupNumber = COALESCE(@ServiceGroupNumber, ServiceGroupNumber),
        ServiceGroupName = COALESCE(@ServiceGroupName, ServiceGroupName)
         WHERE ServiceGroupID = @ServiceGroupID;

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'ServiceGroup',
            @EntityID   = @ServiceGroupID,
            @UserID     = @ActingUserID,
            @Action     = 'Update';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE usp_ServiceGroup_SoftDelete
    @ServiceGroupID INT,
    @ActingUserID INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.tServiceGroup WHERE ServiceGroupID = @ServiceGroupID AND IsActive = 1)
        THROW 50001, 'tServiceGroup not found or already inactive.', 1;

    BEGIN TRY
        BEGIN TRAN;

        UPDATE dbo.tServiceGroup
           SET IsActive = 0
         WHERE ServiceGroupID = @ServiceGroupID;

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'ServiceGroup',
            @EntityID   = @ServiceGroupID,
            @UserID     = @ActingUserID,
            @Action     = 'SoftDelete';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO

--tServiceCateogry
CREATE OR ALTER PROCEDURE usp_CreateTable_tServiceCategory
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID('dbo.tServiceCategory', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.tServiceCategory (
            ServiceCategoryID INT IDENTITY(1,1) NOT NULL,
        CategoryName                 VARCHAR(100)         NOT NULL,
        ServiceCategoryDescription   VARCHAR(100)         NULL,
        IsActive                     BIT                  NOT NULL CONSTRAINT DF_tServiceCategory_IsActive DEFAULT (1),
        CONSTRAINT PK_tServiceCategory PRIMARY KEY CLUSTERED (ServiceCategoryID),
        CONSTRAINT UQ_tServiceCategory_CategoryName UNIQUE (CategoryName)
        );
    END;

    -- Seed rows (idempotent): CategoryName grouped by ServiceCategoryDescription.
    SET IDENTITY_INSERT dbo.tServiceCategory ON;

    MERGE dbo.tServiceCategory AS tgt
    USING (VALUES
        (1, N'Exposure Management',  N'Standard Reinsurance Placement'),
        (2, N'Catastrophe Modeling', N'Standard Reinsurance Placement'),
        (3, N'Reinsurance Placement',N'Standard Reinsurance Placement'),
        (4, N'Model / Peril Advisory',N'Advisory'),
        (5, N'Portfolio Strategy',   N'Advisory'),
        (6, N'Regulatory',           N'Advisory')
    ) AS src (ServiceCategoryID, CategoryName, ServiceCategoryDescription)
      ON tgt.ServiceCategoryID = src.ServiceCategoryID
    WHEN NOT MATCHED BY TARGET THEN
        INSERT (ServiceCategoryID, CategoryName, ServiceCategoryDescription)
        VALUES (src.ServiceCategoryID, src.CategoryName, src.ServiceCategoryDescription)
    WHEN MATCHED THEN
        UPDATE SET CategoryName = src.CategoryName,
                   ServiceCategoryDescription = src.ServiceCategoryDescription;

    SET IDENTITY_INSERT dbo.tServiceCategory OFF;
END;
GO
CREATE OR ALTER PROCEDURE usp_ServiceCategory_Create
    @CategoryName                 VARCHAR(100),
    @ServiceCategoryDescription   VARCHAR(100) = NULL,
    @ActingUserID                INT,
    @NewServiceCategoryID        INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRAN;

        INSERT INTO dbo.tServiceCategory (CategoryName, ServiceCategoryDescription)
        VALUES (@CategoryName, @ServiceCategoryDescription);

        SET @NewServiceCategoryID = SCOPE_IDENTITY();

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'ServiceCategory',
            @EntityID   = @NewServiceCategoryID,
            @UserID     = @ActingUserID,
            @Action     = 'Insert';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE usp_ServiceCategory_Get
    @ServiceCategoryID INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT * FROM dbo.tServiceCategory WHERE ServiceCategoryID = @ServiceCategoryID;
END;
GO
CREATE OR ALTER PROCEDURE usp_ServiceCategory_List
    @ActiveOnly BIT = 1
AS
BEGIN
    SET NOCOUNT ON;
    SELECT *
      FROM dbo.tServiceCategory
     WHERE (@ActiveOnly = 0 OR IsActive = 1);
END;
GO
CREATE OR ALTER PROCEDURE usp_ServiceCategory_Update
    @ServiceCategoryID            INT,
    @CategoryName                 VARCHAR(100) = NULL,
    @ServiceCategoryDescription   VARCHAR(100) = NULL,
    @ActingUserID                INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.tServiceCategory WHERE ServiceCategoryID = @ServiceCategoryID)
        THROW 50001, 'tServiceCategory not found.', 1;

    BEGIN TRY
        BEGIN TRAN;

        UPDATE dbo.tServiceCategory
           SET
        CategoryName = COALESCE(@CategoryName, CategoryName),
        ServiceCategoryDescription = COALESCE(@ServiceCategoryDescription, ServiceCategoryDescription)
         WHERE ServiceCategoryID = @ServiceCategoryID;

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'ServiceCategory',
            @EntityID   = @ServiceCategoryID,
            @UserID     = @ActingUserID,
            @Action     = 'Update';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE usp_ServiceCategory_SoftDelete
    @ServiceCategoryID INT,
    @ActingUserID INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.tServiceCategory WHERE ServiceCategoryID = @ServiceCategoryID AND IsActive = 1)
        THROW 50001, 'tServiceCategory not found or already inactive.', 1;

    BEGIN TRY
        BEGIN TRAN;

        UPDATE dbo.tServiceCategory
           SET IsActive = 0
         WHERE ServiceCategoryID = @ServiceCategoryID;

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'ServiceCategory',
            @EntityID   = @ServiceCategoryID,
            @UserID     = @ActingUserID,
            @Action     = 'SoftDelete';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO

--tService
CREATE OR ALTER PROCEDURE usp_CreateTable_tService
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID('dbo.tService', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.tService (
            ServiceID INT IDENTITY(1,1) NOT NULL,
        ServiceName                  VARCHAR(150)         NOT NULL,
        ServiceDescription           VARCHAR(255)         NULL,
        IsActive                     BIT                  NOT NULL CONSTRAINT DF_tService_IsActive DEFAULT (1),
        CONSTRAINT PK_tService PRIMARY KEY CLUSTERED (ServiceID),
        CONSTRAINT UQ_tService_ServiceName UNIQUE (ServiceName)
        );
    END;
END;
GO
CREATE OR ALTER PROCEDURE usp_Service_Create
    @ServiceName                  VARCHAR(150),
    @ServiceDescription           VARCHAR(255) = NULL,
    @ActingUserID                INT,
    @NewServiceID                INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRAN;

        INSERT INTO dbo.tService (ServiceName, ServiceDescription)
        VALUES (@ServiceName, @ServiceDescription);

        SET @NewServiceID = SCOPE_IDENTITY();

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'Service',
            @EntityID   = @NewServiceID,
            @UserID     = @ActingUserID,
            @Action     = 'Insert';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE usp_Service_Get
    @ServiceID INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT * FROM dbo.tService WHERE ServiceID = @ServiceID;
END;
GO
CREATE OR ALTER PROCEDURE usp_Service_List
    @ActiveOnly BIT = 1
AS
BEGIN
    SET NOCOUNT ON;
    SELECT *
      FROM dbo.tService
     WHERE (@ActiveOnly = 0 OR IsActive = 1);
END;
GO
CREATE OR ALTER PROCEDURE usp_Service_Update
    @ServiceID                    INT,
    @ServiceName                  VARCHAR(150) = NULL,
    @ServiceDescription           VARCHAR(255) = NULL,
    @ActingUserID                INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.tService WHERE ServiceID = @ServiceID)
        THROW 50001, 'tService not found.', 1;

    BEGIN TRY
        BEGIN TRAN;

        UPDATE dbo.tService
           SET
        ServiceName = COALESCE(@ServiceName, ServiceName),
        ServiceDescription = COALESCE(@ServiceDescription, ServiceDescription)
         WHERE ServiceID = @ServiceID;

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'Service',
            @EntityID   = @ServiceID,
            @UserID     = @ActingUserID,
            @Action     = 'Update';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE usp_Service_SoftDelete
    @ServiceID INT,
    @ActingUserID INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.tService WHERE ServiceID = @ServiceID AND IsActive = 1)
        THROW 50001, 'tService not found or already inactive.', 1;

    BEGIN TRY
        BEGIN TRAN;

        UPDATE dbo.tService
           SET IsActive = 0
         WHERE ServiceID = @ServiceID;

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'Service',
            @EntityID   = @ServiceID,
            @UserID     = @ActingUserID,
            @Action     = 'SoftDelete';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO

--tClient
CREATE OR ALTER PROCEDURE usp_CreateTable_tClient
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID('dbo.tClient', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.tClient (
            ClientID INT IDENTITY(1,1) NOT NULL,
        AccountExecutiveUserID       INT                  NULL,
        AnalyticsLeadUserID          INT                  NULL,
        ClientName                   VARCHAR(150)         NOT NULL,
        ClientDescription            VARCHAR(500)         NULL,
        ClientShortName              VARCHAR(50)          NULL,
        CreatedDate                  DATETIME2(0)         NOT NULL CONSTRAINT DF_tClient_CreatedDate  DEFAULT (SYSUTCDATETIME()),
        ModifiedDate                 DATETIME2(0)         NOT NULL CONSTRAINT DF_tClient_ModifiedDate DEFAULT (SYSUTCDATETIME()),
        IsActive                     BIT                  NOT NULL CONSTRAINT DF_tClient_IsActive DEFAULT (1),
        ClientStatus                 VARCHAR(10)          NOT NULL CONSTRAINT DF_tClient_ClientStatus DEFAULT ('Active')
                                                          CONSTRAINT CK_tClient_ClientStatus CHECK (ClientStatus IN ('Active', 'Inactive', 'Prospect')),
        CONSTRAINT PK_tClient PRIMARY KEY CLUSTERED (ClientID)
        );

        CREATE INDEX IX_tClient_AccountExecutiveUserID ON dbo.tClient (AccountExecutiveUserID);
        CREATE INDEX IX_tClient_AnalyticsLeadUserID ON dbo.tClient (AnalyticsLeadUserID);
    END;
END;
GO
/*
  tClient.AccountExecutiveUserID / AnalyticsLeadUserID reference dbo.tUser, which
  is created later in the install order. Run this after usp_CreateTable_tUser
  (manually or via usp_Bootstrap chunk 3b).
*/
CREATE OR ALTER PROCEDURE usp_AlterTable_tClient_AddUserFKs
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID('dbo.tClient', 'U') IS NULL
        THROW 50001, 'dbo.tClient does not exist. Run usp_CreateTable_tClient first.', 1;

    IF OBJECT_ID('dbo.tUser', 'U') IS NULL
        THROW 50001, 'dbo.tUser does not exist. Run usp_CreateTable_tUser before adding tClient user FKs.', 1;

    IF NOT EXISTS (
        SELECT 1 FROM sys.foreign_keys
         WHERE name = N'FK_tClient_AccountExecutiveUserID'
           AND parent_object_id = OBJECT_ID(N'dbo.tClient')
    )
    BEGIN
        ALTER TABLE dbo.tClient
            ADD CONSTRAINT FK_tClient_AccountExecutiveUserID
                FOREIGN KEY (AccountExecutiveUserID) REFERENCES dbo.tUser (UserID);
    END;

    IF NOT EXISTS (
        SELECT 1 FROM sys.foreign_keys
         WHERE name = N'FK_tClient_AnalyticsLeadUserID'
           AND parent_object_id = OBJECT_ID(N'dbo.tClient')
    )
    BEGIN
        ALTER TABLE dbo.tClient
            ADD CONSTRAINT FK_tClient_AnalyticsLeadUserID
                FOREIGN KEY (AnalyticsLeadUserID) REFERENCES dbo.tUser (UserID);
    END;
END;
GO
/*
  Adds the ClientStatus column (Active / Inactive / Prospect) to an existing dbo.tClient.
  ClientStatus is the source of truth for a client's lifecycle; IsActive is kept in sync
  (Active & Prospect => IsActive = 1, Inactive => IsActive = 0). Idempotent: safe to re-run.
*/
CREATE OR ALTER PROCEDURE usp_AlterTable_tClient_AddStatus
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID('dbo.tClient', 'U') IS NULL
        THROW 50001, 'dbo.tClient does not exist. Run usp_CreateTable_tClient first.', 1;

    IF COL_LENGTH('dbo.tClient', 'ClientStatus') IS NULL
    BEGIN
        ALTER TABLE dbo.tClient
            ADD ClientStatus VARCHAR(10) NOT NULL
                CONSTRAINT DF_tClient_ClientStatus DEFAULT ('Active');
    END;

    IF NOT EXISTS (
        SELECT 1 FROM sys.check_constraints
         WHERE name = N'CK_tClient_ClientStatus'
           AND parent_object_id = OBJECT_ID(N'dbo.tClient')
    )
    BEGIN
        ALTER TABLE dbo.tClient
            ADD CONSTRAINT CK_tClient_ClientStatus
                CHECK (ClientStatus IN ('Active', 'Inactive', 'Prospect'));
    END;

    -- Backfill from the existing soft-delete flag for rows created before this column existed.
    UPDATE dbo.tClient
       SET ClientStatus = CASE WHEN IsActive = 0 THEN 'Inactive' ELSE 'Active' END
     WHERE ClientStatus NOT IN ('Active', 'Inactive', 'Prospect')
        OR (IsActive = 0 AND ClientStatus <> 'Inactive')
        OR (IsActive = 1 AND ClientStatus = 'Inactive');
END;
GO
CREATE OR ALTER PROCEDURE usp_Client_Create
    @AccountExecutiveUserID       INT = NULL,
    @AnalyticsLeadUserID          INT = NULL,
    @ClientName                   VARCHAR(150),
    @ClientDescription            VARCHAR(500) = NULL,
    @ClientShortName              VARCHAR(50) = NULL,
    @ClientStatus                 VARCHAR(10) = 'Active',
    @ActingUserID                INT,
    @NewClientID                 INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @ClientStatus = COALESCE(NULLIF(LTRIM(RTRIM(@ClientStatus)), ''), 'Active');
    IF @ClientStatus NOT IN ('Active', 'Inactive', 'Prospect')
        THROW 50001, 'ClientStatus must be Active, Inactive, or Prospect.', 1;

    BEGIN TRY
        BEGIN TRAN;

        INSERT INTO dbo.tClient (AccountExecutiveUserID, AnalyticsLeadUserID, ClientName, ClientDescription, ClientShortName, ClientStatus, IsActive)
        VALUES (@AccountExecutiveUserID, @AnalyticsLeadUserID, @ClientName, @ClientDescription, @ClientShortName, @ClientStatus,
                CASE WHEN @ClientStatus = 'Inactive' THEN 0 ELSE 1 END);

        SET @NewClientID = SCOPE_IDENTITY();

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'Client',
            @EntityID   = @NewClientID,
            @UserID     = @ActingUserID,
            @Action     = 'Insert';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE usp_Client_Get
    @ClientID INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT * FROM dbo.tClient WHERE ClientID = @ClientID;
END;
GO
CREATE OR ALTER PROCEDURE usp_Client_List
    @ActiveOnly BIT = 1
AS
BEGIN
    SET NOCOUNT ON;
    SELECT *
      FROM dbo.tClient
     WHERE (@ActiveOnly = 0 OR IsActive = 1);
END;
GO
CREATE OR ALTER PROCEDURE usp_Client_Update
    @ClientID                     INT,
    @AccountExecutiveUserID       INT = NULL,
    @AnalyticsLeadUserID          INT = NULL,
    @ClientName                   VARCHAR(150) = NULL,
    @ClientDescription            VARCHAR(500) = NULL,
    @ClientShortName              VARCHAR(50) = NULL,
    @ClientStatus                 VARCHAR(10) = NULL,
    @ActingUserID                INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.tClient WHERE ClientID = @ClientID)
        THROW 50001, 'tClient not found.', 1;

    SET @ClientStatus = NULLIF(LTRIM(RTRIM(@ClientStatus)), '');
    IF @ClientStatus IS NOT NULL AND @ClientStatus NOT IN ('Active', 'Inactive', 'Prospect')
        THROW 50001, 'ClientStatus must be Active, Inactive, or Prospect.', 1;

    BEGIN TRY
        BEGIN TRAN;

        UPDATE dbo.tClient
           SET
        AccountExecutiveUserID = COALESCE(@AccountExecutiveUserID, AccountExecutiveUserID),
        AnalyticsLeadUserID = COALESCE(@AnalyticsLeadUserID, AnalyticsLeadUserID),
        ClientName = COALESCE(@ClientName, ClientName),
        ClientDescription = COALESCE(@ClientDescription, ClientDescription),
        ClientShortName = COALESCE(@ClientShortName, ClientShortName),
        ClientStatus = COALESCE(@ClientStatus, ClientStatus),
        IsActive = CASE WHEN COALESCE(@ClientStatus, ClientStatus) = 'Inactive' THEN 0 ELSE 1 END,
        ModifiedDate = SYSUTCDATETIME()
         WHERE ClientID = @ClientID;

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'Client',
            @EntityID   = @ClientID,
            @UserID     = @ActingUserID,
            @Action     = 'Update';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE usp_Client_SoftDelete
    @ClientID INT,
    @ActingUserID INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.tClient WHERE ClientID = @ClientID AND IsActive = 1)
        THROW 50001, 'tClient not found or already inactive.', 1;

    BEGIN TRY
        BEGIN TRAN;

        UPDATE dbo.tClient
           SET IsActive = 0,
            ClientStatus = 'Inactive',
            ModifiedDate = SYSUTCDATETIME()
         WHERE ClientID = @ClientID;

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'Client',
            @EntityID   = @ClientID,
            @UserID     = @ActingUserID,
            @Action     = 'SoftDelete';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE usp_Client_HardDelete
    @ClientID     INT,
    @ActingUserID INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.tClient WHERE ClientID = @ClientID)
        THROW 50001, 'tClient not found.', 1;

    DECLARE @ActiveProjectCount INT =
    (
        SELECT COUNT(DISTINCT p.ProjectID)
          FROM dbo.tProject AS p
         INNER JOIN dbo.tProjectRef AS pr
            ON pr.ProjectID = p.ProjectID
           AND pr.IsActive = 1
         INNER JOIN dbo.tClientRef AS cr
            ON cr.ClientRefID = pr.ClientRefID
         WHERE cr.ClientID = @ClientID
           AND p.IsActive = 1
    );

    IF @ActiveProjectCount > 0
        THROW 50002, 'This client cannot be deleted because it is associated with active projects. Please remove the project before deleting the client.', 1;

    DECLARE @ClientName VARCHAR(150);
    SELECT @ClientName = ClientName
      FROM dbo.tClient
     WHERE ClientID = @ClientID;

    BEGIN TRY
        BEGIN TRAN;

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'Client',
            @EntityID   = @ClientID,
            @UserID     = @ActingUserID,
            @Action     = 'Remove',
            @FieldName  = NULL,
            @OldValue   = @ClientName,
            @NewValue   = NULL;

        DECLARE @Projects TABLE (ProjectID INT NOT NULL PRIMARY KEY);

        INSERT INTO @Projects (ProjectID)
        SELECT DISTINCT p.ProjectID
          FROM dbo.tProject AS p
         INNER JOIN dbo.tProjectRef AS pr
            ON pr.ProjectID = p.ProjectID
         INNER JOIN dbo.tClientRef AS cr
            ON cr.ClientRefID = pr.ClientRefID
         WHERE cr.ClientID = @ClientID;

        IF EXISTS (SELECT 1 FROM @Projects)
        BEGIN
            IF OBJECT_ID(N'dbo.tProjectTimeEntry', N'U') IS NOT NULL
            BEGIN
                DELETE te
                  FROM dbo.tProjectTimeEntry AS te
                 INNER JOIN dbo.tProjectRef AS pr ON pr.ProjectRefID = te.ProjectRefID
                 INNER JOIN @Projects AS x ON x.ProjectID = pr.ProjectID;
            END;

            IF OBJECT_ID(N'dbo.tProjectCalendarItem', N'U') IS NOT NULL
            BEGIN
                DELETE ci
                  FROM dbo.tProjectCalendarItem AS ci
                 INNER JOIN dbo.tProjectRef AS pr ON pr.ProjectRefID = ci.ProjectRefID
                 INNER JOIN @Projects AS x ON x.ProjectID = pr.ProjectID;
            END;

            IF OBJECT_ID(N'dbo.tProjectFile', N'U') IS NOT NULL
            BEGIN
                DELETE pf
                  FROM dbo.tProjectFile AS pf
                 INNER JOIN dbo.tProjectRef AS pr ON pr.ProjectRefID = pf.ProjectRefID
                 INNER JOIN @Projects AS x ON x.ProjectID = pr.ProjectID;
            END;

            IF OBJECT_ID(N'dbo.tProjectTag', N'U') IS NOT NULL
            BEGIN
                DELETE pt
                  FROM dbo.tProjectTag AS pt
                 INNER JOIN dbo.tProjectRef AS pr ON pr.ProjectRefID = pt.ProjectRefID
                 INNER JOIN @Projects AS x ON x.ProjectID = pr.ProjectID;
            END;

            DELETE pr
              FROM dbo.tProjectRef AS pr
             INNER JOIN @Projects AS x ON x.ProjectID = pr.ProjectID;

            DELETE p
              FROM dbo.tProject AS p
             INNER JOIN @Projects AS x ON x.ProjectID = p.ProjectID;
        END;

        IF OBJECT_ID(N'dbo.tClientTag', N'U') IS NOT NULL
        BEGIN
            DELETE FROM dbo.tClientTag
             WHERE ClientID = @ClientID;
        END;

        DELETE FROM dbo.tClient
         WHERE ClientID = @ClientID;

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO

--tProject
CREATE OR ALTER PROCEDURE usp_CreateTable_tProject
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID('dbo.tProject', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.tProject (
            ProjectID INT IDENTITY(1,1) NOT NULL,
        ProjectNumber                VARCHAR(50)          NOT NULL,
        ProjectName                  VARCHAR(200)         NOT NULL,
        RenewalDate                  DATE                 NULL,
        DueDate                      DATE                 NULL,
        CreatedDate                  DATETIME2(0)         NOT NULL CONSTRAINT DF_tProject_CreatedDate  DEFAULT (SYSUTCDATETIME()),
        ModifiedDate                 DATETIME2(0)         NOT NULL CONSTRAINT DF_tProject_ModifiedDate DEFAULT (SYSUTCDATETIME()),
        IsActive                     BIT                  NOT NULL CONSTRAINT DF_tProject_IsActive DEFAULT (1),
        IsOnline                     BIT                  NOT NULL CONSTRAINT DF_tProject_IsOnline DEFAULT (0),
        CONSTRAINT PK_tProject PRIMARY KEY CLUSTERED (ProjectID)
        );

        CREATE UNIQUE INDEX UX_tProject_Number_Active ON dbo.tProject (ProjectNumber) WHERE IsActive = 1;

        
    END;
END;
GO
/*
  Adds IsOnline (BIT) to dbo.tProject so users can mark a project as currently
  in progress / online. Idempotent for existing installs.
*/
CREATE OR ALTER PROCEDURE usp_AlterTable_tProject_AddIsOnline
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID('dbo.tProject', 'U') IS NULL
        THROW 50001, 'dbo.tProject does not exist. Run usp_CreateTable_tProject first.', 1;

    IF COL_LENGTH('dbo.tProject', 'IsOnline') IS NULL
    BEGIN
        ALTER TABLE dbo.tProject
            ADD IsOnline BIT NOT NULL CONSTRAINT DF_tProject_IsOnline DEFAULT (0);
    END;
END;
GO
CREATE OR ALTER PROCEDURE usp_Project_Create
    @ProjectNumber                VARCHAR(50),
    @ProjectName                  VARCHAR(200),
    @RenewalDate                  DATE = NULL,
    @DueDate                      DATE = NULL,
    @ActingUserID                INT,
    @NewProjectID                INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRAN;

        INSERT INTO dbo.tProject (ProjectNumber, ProjectName, RenewalDate, DueDate)
        VALUES (@ProjectNumber, @ProjectName, @RenewalDate, @DueDate);

        SET @NewProjectID = SCOPE_IDENTITY();

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'Project',
            @EntityID   = @NewProjectID,
            @UserID     = @ActingUserID,
            @Action     = 'Insert';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE usp_Project_Get
    @ProjectID INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT * FROM dbo.tProject WHERE ProjectID = @ProjectID;
END;
GO
CREATE OR ALTER PROCEDURE usp_Project_List
    @ActiveOnly BIT = 1
AS
BEGIN
    SET NOCOUNT ON;
    SELECT *
      FROM dbo.tProject
     WHERE (@ActiveOnly = 0 OR IsActive = 1);
END;
GO
CREATE OR ALTER PROCEDURE usp_Project_Update
    @ProjectID                    INT,
    @ProjectNumber                VARCHAR(50) = NULL,
    @ProjectName                  VARCHAR(200) = NULL,
    @RenewalDate                  DATE = NULL,
    @DueDate                      DATE = NULL,
    @ActingUserID                INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.tProject WHERE ProjectID = @ProjectID)
        THROW 50001, 'tProject not found.', 1;

    BEGIN TRY
        BEGIN TRAN;

        UPDATE dbo.tProject
           SET
        ProjectNumber = COALESCE(@ProjectNumber, ProjectNumber),
        ProjectName = COALESCE(@ProjectName, ProjectName),
        RenewalDate = COALESCE(@RenewalDate, RenewalDate),
        DueDate = COALESCE(@DueDate, DueDate),
        ModifiedDate = SYSUTCDATETIME()
         WHERE ProjectID = @ProjectID;

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'Project',
            @EntityID   = @ProjectID,
            @UserID     = @ActingUserID,
            @Action     = 'Update';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE usp_Project_SoftDelete
    @ProjectID INT,
    @ActingUserID INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.tProject WHERE ProjectID = @ProjectID AND IsActive = 1)
        THROW 50001, 'tProject not found or already inactive.', 1;

    BEGIN TRY
        BEGIN TRAN;

        UPDATE dbo.tProject
           SET IsActive = 0,
            ModifiedDate = SYSUTCDATETIME()
         WHERE ProjectID = @ProjectID;

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'Project',
            @EntityID   = @ProjectID,
            @UserID     = @ActingUserID,
            @Action     = 'SoftDelete';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO

--tUser
CREATE OR ALTER PROCEDURE usp_CreateTable_tUser
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID('dbo.tUser', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.tUser (
        UserID INT IDENTITY(1,1) NOT NULL,
        FirstName                    VARCHAR(50)          NOT NULL,
        LastName                     VARCHAR(50)          NOT NULL,
        Title                        VARCHAR(100)         NULL,
        Email                        VARCHAR(150)         NOT NULL,
        Phone                        VARCHAR(30)          NULL,
        Company                      VARCHAR(150)         NULL,
        Department                   VARCHAR(100)         NULL,
        UserType                     VARCHAR(20)          NOT NULL,
        CreatedDate                  DATETIME2(0)         NOT NULL CONSTRAINT DF_tUser_CreatedDate  DEFAULT (SYSUTCDATETIME()),
        ModifiedDate                 DATETIME2(0)         NOT NULL CONSTRAINT DF_tUser_ModifiedDate DEFAULT (SYSUTCDATETIME()),
        IsActive                     BIT                  NOT NULL CONSTRAINT DF_tUser_IsActive DEFAULT (1),
        CONSTRAINT PK_tUser PRIMARY KEY CLUSTERED (UserID),
        CONSTRAINT UQ_tUser_Email UNIQUE (Email),
        CONSTRAINT CK_tUser_UserType CHECK (UserType IN ('Basic','External','Administrator'))
		)
    END;
END;
GO
CREATE OR ALTER PROCEDURE usp_User_Create
    @FirstName                    VARCHAR(50),
    @LastName                     VARCHAR(50),
    @Title                        VARCHAR(100) = NULL,
    @Email                        VARCHAR(150),
    @Phone                        VARCHAR(30) = NULL,
    @Company                      VARCHAR(150) = NULL,
    @Department                   VARCHAR(100) = NULL,
    @UserType                     VARCHAR(20),
    @ActingUserID                INT,
    @NewUserID                   INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRAN;

        INSERT INTO dbo.tUser (FirstName, LastName, Title, Email, Phone, Company, Department, UserType)
        VALUES (@FirstName, @LastName, @Title, @Email, @Phone, @Company, @Department, @UserType);

        SET @NewUserID = SCOPE_IDENTITY();

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'User',
            @EntityID   = @NewUserID,
            @UserID     = @ActingUserID,
            @Action     = 'Insert';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO
SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO
CREATE OR ALTER PROCEDURE usp_User_CreateWithPrimaryAddress
    @FirstName                    VARCHAR(50),
    @LastName                     VARCHAR(50),
    @Title                        VARCHAR(100) = NULL,
    @Email                        VARCHAR(150),
    @Phone                        VARCHAR(30) = NULL,
    @Company                      VARCHAR(150) = NULL,
    @Department                   VARCHAR(100) = NULL,
    @UserType                     VARCHAR(20),
    @AddressType                  VARCHAR(20) = NULL,
    @Address1                     VARCHAR(200) = NULL,
    @Address2                     VARCHAR(200) = NULL,
    @City                         VARCHAR(100) = NULL,
    @State                        VARCHAR(50)  = NULL,
    @PostalCode                   VARCHAR(20)  = NULL,
    @Country                      VARCHAR(50)  = NULL,
    @ActingUserID                INT,
    @NewUserID                   INT OUTPUT,
    @NewAddressID                INT = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @UserType NOT IN ('Basic', 'External', 'Administrator')
        THROW 50003, 'UserType must be Basic, External, or Administrator.', 1;

    IF EXISTS (SELECT 1 FROM dbo.tUser WHERE Email = @Email AND IsActive = 1)
        THROW 50004, 'Email already exists.', 1;

    SET @NewAddressID = NULL;

    BEGIN TRY
        BEGIN TRAN;

        INSERT INTO dbo.tUser (FirstName, LastName, Title, Email, Phone, Company, Department, UserType)
        VALUES (@FirstName, @LastName, @Title, @Email, @Phone, @Company, @Department, @UserType);

        SET @NewUserID = SCOPE_IDENTITY();

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'User',
            @EntityID   = @NewUserID,
            @UserID     = @ActingUserID,
            @Action     = 'Insert';

        IF NULLIF(LTRIM(RTRIM(@Address1)), '') IS NOT NULL
        BEGIN
            DECLARE @AddrType VARCHAR(20) = COALESCE(NULLIF(LTRIM(RTRIM(@AddressType)), ''), 'Work');
            DECLARE @AddrCountry VARCHAR(50) = COALESCE(NULLIF(LTRIM(RTRIM(@Country)), ''), 'US');

            INSERT INTO dbo.tAddress (Address1, Address2, City, State, PostalCode, Country)
            VALUES (
                LTRIM(RTRIM(@Address1)),
                @Address2,
                @City,
                @State,
                @PostalCode,
                @AddrCountry
            );

            SET @NewAddressID = SCOPE_IDENTITY();

            EXEC dbo.usp_ActivityLog_Insert
                @EntityType = 'Address',
                @EntityID   = @NewAddressID,
                @UserID     = @ActingUserID,
                @Action     = 'Insert';

            INSERT INTO dbo.tUserAddressRef (UserID, AddressID, AddressType, IsPrimary)
            VALUES (@NewUserID, @NewAddressID, @AddrType, 1);

            EXEC dbo.usp_ActivityLog_Insert
                @EntityType = 'UserAddressRef',
                @EntityID   = @NewUserID,
                @UserID     = @ActingUserID,
                @Action     = 'Insert';
        END;

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE usp_User_Get
    @UserID INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT * FROM dbo.tUser WHERE UserID = @UserID;
END;
GO
CREATE OR ALTER PROCEDURE usp_User_List
    @ActiveOnly BIT = 1
AS
BEGIN
    SET NOCOUNT ON;
    SELECT *
      FROM dbo.tUser
     WHERE (@ActiveOnly = 0 OR IsActive = 1);
END;
GO
CREATE OR ALTER PROCEDURE usp_User_Update
    @UserID                       INT,
    @FirstName                    VARCHAR(50) = NULL,
    @LastName                     VARCHAR(50) = NULL,
    @Title                        VARCHAR(100) = NULL,
    @Email                        VARCHAR(150) = NULL,
    @Phone                        VARCHAR(30) = NULL,
    @Company                      VARCHAR(150) = NULL,
    @Department                   VARCHAR(100) = NULL,
    @UserType                     VARCHAR(20) = NULL,
    @ActingUserID                INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.tUser WHERE UserID = @UserID)
        THROW 50001, 'tUser not found.', 1;

    BEGIN TRY
        BEGIN TRAN;

        UPDATE dbo.tUser
           SET
        FirstName = COALESCE(@FirstName, FirstName),
        LastName = COALESCE(@LastName, LastName),
        Title = COALESCE(@Title, Title),
        Email = COALESCE(@Email, Email),
        Phone = COALESCE(@Phone, Phone),
        Company = COALESCE(@Company, Company),
        Department = COALESCE(@Department, Department),
        UserType = COALESCE(@UserType, UserType),
        ModifiedDate = SYSUTCDATETIME()
         WHERE UserID = @UserID;

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'User',
            @EntityID   = @UserID,
            @UserID     = @ActingUserID,
            @Action     = 'Update';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE usp_User_SoftDelete
    @UserID INT,
    @ActingUserID INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.tUser WHERE UserID = @UserID AND IsActive = 1)
        THROW 50001, 'tUser not found or already inactive.', 1;

    BEGIN TRY
        BEGIN TRAN;

        UPDATE dbo.tUser
           SET IsActive = 0,
            ModifiedDate = SYSUTCDATETIME()
         WHERE UserID = @UserID;

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'User',
            @EntityID   = @UserID,
            @UserID     = @ActingUserID,
            @Action     = 'SoftDelete';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO

--tAddress
CREATE OR ALTER PROCEDURE usp_CreateTable_tAddress
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID('dbo.tAddress', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.tAddress (
            AddressID    INT IDENTITY(1,1) NOT NULL,
            Address1     VARCHAR(200) NOT NULL,
            Address2     VARCHAR(200) NULL,
            City         VARCHAR(100) NULL,
            State        VARCHAR(50)  NULL,
            PostalCode   VARCHAR(20)  NULL,
            Country      VARCHAR(50)  NULL,
            CreatedDate  DATETIME2(0) NOT NULL CONSTRAINT DF_tAddress_CreatedDate  DEFAULT (SYSUTCDATETIME()),
            ModifiedDate DATETIME2(0) NOT NULL CONSTRAINT DF_tAddress_ModifiedDate DEFAULT (SYSUTCDATETIME()),
            IsActive     BIT          NOT NULL CONSTRAINT DF_tAddress_IsActive     DEFAULT (1),
            CONSTRAINT PK_tAddress PRIMARY KEY CLUSTERED (AddressID)
        );
    END;
END;
GO
CREATE OR ALTER PROCEDURE usp_Address_Create
    @Address1     VARCHAR(200),
    @Address2     VARCHAR(200) = NULL,
    @City         VARCHAR(100) = NULL,
    @State        VARCHAR(50)  = NULL,
    @PostalCode   VARCHAR(20)  = NULL,
    @Country      VARCHAR(50)  = NULL,
    @ActingUserID INT,
    @NewAddressID INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRAN;

        INSERT INTO dbo.tAddress (Address1, Address2, City, State, PostalCode, Country)
        VALUES (@Address1, @Address2, @City, @State, @PostalCode, @Country);

        SET @NewAddressID = SCOPE_IDENTITY();

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'Address',
            @EntityID   = @NewAddressID,
            @UserID     = @ActingUserID,
            @Action     = 'Insert';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE usp_Address_Get
    @AddressID INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT * FROM dbo.tAddress WHERE AddressID = @AddressID;
END;
GO
CREATE OR ALTER PROCEDURE usp_Address_List
    @ActiveOnly BIT = 1
AS
BEGIN
    SET NOCOUNT ON;
    SELECT *
      FROM dbo.tAddress
     WHERE (@ActiveOnly = 0 OR IsActive = 1);
END;
GO
CREATE OR ALTER PROCEDURE usp_Address_Update
    @AddressID    INT,
    @Address1     VARCHAR(200) = NULL,
    @Address2     VARCHAR(200) = NULL,
    @City         VARCHAR(100) = NULL,
    @State        VARCHAR(50)  = NULL,
    @PostalCode   VARCHAR(20)  = NULL,
    @Country      VARCHAR(50)  = NULL,
    @ActingUserID INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.tAddress WHERE AddressID = @AddressID)
        THROW 50001, 'tAddress not found.', 1;

    BEGIN TRY
        BEGIN TRAN;

        UPDATE dbo.tAddress
           SET Address1     = COALESCE(@Address1, Address1),
               Address2     = COALESCE(@Address2, Address2),
               City         = COALESCE(@City, City),
               State        = COALESCE(@State, State),
               PostalCode   = COALESCE(@PostalCode, PostalCode),
               Country      = COALESCE(@Country, Country),
               ModifiedDate = SYSUTCDATETIME()
         WHERE AddressID = @AddressID;

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'Address',
            @EntityID   = @AddressID,
            @UserID     = @ActingUserID,
            @Action     = 'Update';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE usp_Address_SoftDelete
    @AddressID    INT,
    @ActingUserID INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.tAddress WHERE AddressID = @AddressID AND IsActive = 1)
        THROW 50001, 'tAddress not found or already inactive.', 1;

    BEGIN TRY
        BEGIN TRAN;

        UPDATE dbo.tAddress
           SET IsActive     = 0,
               ModifiedDate = SYSUTCDATETIME()
         WHERE AddressID = @AddressID;

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'Address',
            @EntityID   = @AddressID,
            @UserID     = @ActingUserID,
            @Action     = 'SoftDelete';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO

--tStatus (lookup table, modeled on tBusinessUnit). StatusCode: A/I/P.
CREATE OR ALTER PROCEDURE usp_CreateTable_tStatus
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID('dbo.tStatus', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.tStatus (
            StatusID INT IDENTITY(1,1) NOT NULL,
        StatusCode                   VARCHAR(1)           NOT NULL,
        StatusDescription            VARCHAR(50)          NOT NULL,
        IsActive                     BIT                  NOT NULL CONSTRAINT DF_tStatus_IsActive DEFAULT (1),
        CONSTRAINT PK_tStatus PRIMARY KEY CLUSTERED (StatusID),
        CONSTRAINT UQ_tStatus_StatusCode UNIQUE (StatusCode),
        CONSTRAINT UQ_tStatus_StatusDescription UNIQUE (StatusDescription)
        );
    END;

    -- Seed rows (idempotent)
    SET IDENTITY_INSERT dbo.tStatus ON;

    MERGE dbo.tStatus AS tgt
    USING (VALUES
        (1, 'A', 'Active',   1),
        (2, 'I', 'Inactive', 1),
        (3, 'P', 'Prospect', 1)
    ) AS src (StatusID, StatusCode, StatusDescription, IsActive)
      ON tgt.StatusID = src.StatusID
    WHEN NOT MATCHED BY TARGET THEN
        INSERT (StatusID, StatusCode, StatusDescription, IsActive)
        VALUES (src.StatusID, src.StatusCode, src.StatusDescription, src.IsActive);

    SET IDENTITY_INSERT dbo.tStatus OFF;
END;
GO
CREATE OR ALTER PROCEDURE usp_Status_List
    @ActiveOnly BIT = 1
AS
BEGIN
    SET NOCOUNT ON;
    SELECT StatusID, StatusCode, StatusDescription, IsActive
      FROM dbo.tStatus
     WHERE (@ActiveOnly = 0 OR IsActive = 1)
     ORDER BY StatusID;
END;
GO
--tClientRef
CREATE OR ALTER PROCEDURE usp_CreateTable_tClientRef
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID('dbo.tClientRef', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.tClientRef (
            ClientRefID INT IDENTITY(1,1) NOT NULL,
        ClientID                     INT                  NOT NULL,
        BusinessUnitID               INT                  NULL,
        BusinessClassID              INT                  NULL,
        ClientTypeID                 INT                  NULL,
        ClientTypeRefID              INT                  NULL,
        StatusID                     INT                  NOT NULL CONSTRAINT DF_tClientRef_StatusID DEFAULT (1),
        CreatedDate                  DATETIME2(0)         NOT NULL CONSTRAINT DF_tClientRef_CreatedDate  DEFAULT (SYSUTCDATETIME()),
        ModifiedDate                 DATETIME2(0)         NOT NULL CONSTRAINT DF_tClientRef_ModifiedDate DEFAULT (SYSUTCDATETIME()),
        IsActive                     BIT                  NOT NULL CONSTRAINT DF_tClientRef_IsActive DEFAULT (1),
        CONSTRAINT PK_tClientRef PRIMARY KEY CLUSTERED (ClientRefID),
        CONSTRAINT FK_tClientRef_ClientID FOREIGN KEY (ClientID) REFERENCES dbo.tClient (ClientID) ON DELETE CASCADE,
        CONSTRAINT FK_tClientRef_BusinessUnitID FOREIGN KEY (BusinessUnitID) REFERENCES dbo.tBusinessUnit (BusinessUnitID),
        CONSTRAINT FK_tClientRef_BusinessClassID FOREIGN KEY (BusinessClassID) REFERENCES dbo.tBusinessClass (BusinessClassID),
        CONSTRAINT FK_tClientRef_ClientTypeID FOREIGN KEY (ClientTypeID) REFERENCES dbo.tClientType (ClientTypeID),
        CONSTRAINT FK_tClientRef_ClientTypeRefID FOREIGN KEY (ClientTypeRefID) REFERENCES dbo.tClientTypeRef (ClientTypeRefID),
        CONSTRAINT FK_tClientRef_StatusID FOREIGN KEY (StatusID) REFERENCES dbo.tStatus (StatusID)
        );

        CREATE INDEX IX_tClientRef_ClientID ON dbo.tClientRef (ClientID);
        CREATE INDEX IX_tClientRef_BusinessUnitID ON dbo.tClientRef (BusinessUnitID);
        CREATE INDEX IX_tClientRef_BusinessClassID ON dbo.tClientRef (BusinessClassID);
        CREATE INDEX IX_tClientRef_ClientTypeID ON dbo.tClientRef (ClientTypeID);
        CREATE INDEX IX_tClientRef_ClientTypeRefID ON dbo.tClientRef (ClientTypeRefID);
        CREATE INDEX IX_tClientRef_StatusID ON dbo.tClientRef (StatusID);

        -- Filtered unique index: one active row per client per business unit
        IF NOT EXISTS (
            SELECT 1 FROM sys.indexes
            WHERE name = 'UX_tClientRef_Client_BU_Active'
            AND object_id = OBJECT_ID('dbo.tClientRef')
        )
        BEGIN
            CREATE UNIQUE INDEX UX_tClientRef_Client_BU_Active
            ON dbo.tClientRef (ClientID, BusinessUnitID)
            WHERE IsActive = 1;
        END;
    END;
END;
GO
/*
  Adds StatusID (FK -> tStatus) to an existing dbo.tClientRef so each client/business-unit
  relationship can carry its own Active / Inactive / Prospect status. Idempotent; backfills
  from the parent client's status.
*/
CREATE OR ALTER PROCEDURE usp_AlterTable_tClientRef_AddStatus
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID('dbo.tClientRef', 'U') IS NULL
        THROW 50001, 'dbo.tClientRef does not exist. Run usp_CreateTable_tClientRef first.', 1;
    IF OBJECT_ID('dbo.tStatus', 'U') IS NULL
        THROW 50001, 'dbo.tStatus does not exist. Run usp_CreateTable_tStatus first.', 1;

    IF COL_LENGTH('dbo.tClientRef', 'StatusID') IS NULL
    BEGIN
        ALTER TABLE dbo.tClientRef
            ADD StatusID INT NOT NULL CONSTRAINT DF_tClientRef_StatusID DEFAULT (1);
    END;

    IF NOT EXISTS (
        SELECT 1 FROM sys.foreign_keys
         WHERE name = N'FK_tClientRef_StatusID'
           AND parent_object_id = OBJECT_ID(N'dbo.tClientRef')
    )
    BEGIN
        ALTER TABLE dbo.tClientRef
            ADD CONSTRAINT FK_tClientRef_StatusID FOREIGN KEY (StatusID) REFERENCES dbo.tStatus (StatusID);
    END;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tClientRef_StatusID' AND object_id = OBJECT_ID(N'dbo.tClientRef'))
        CREATE INDEX IX_tClientRef_StatusID ON dbo.tClientRef (StatusID);

    -- Backfill each ref's status from the owning client's current status.
    UPDATE r
       SET r.StatusID = s.StatusID
      FROM dbo.tClientRef r
      JOIN dbo.tClient c ON c.ClientID = r.ClientID
      JOIN dbo.tStatus s ON s.StatusDescription = c.ClientStatus
     WHERE r.StatusID <> s.StatusID;
END;
GO
/*
  Adds StatusID (FK -> tStatus) and UserGroupID (FK -> tUserGroup) to dbo.tProjectRef so project
  status and user-group selection persist on save. Idempotent for existing installs.
*/
CREATE OR ALTER PROCEDURE usp_AlterTable_tProjectRef_AddStatusAndUserGroup
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID('dbo.tProjectRef', 'U') IS NULL
        THROW 50001, 'dbo.tProjectRef does not exist. Run usp_CreateTable_tProjectRef first.', 1;
    IF OBJECT_ID('dbo.tStatus', 'U') IS NULL
        THROW 50001, 'dbo.tStatus does not exist. Run usp_CreateTable_tStatus first.', 1;
    IF OBJECT_ID('dbo.tUserGroup', 'U') IS NULL
        THROW 50001, 'dbo.tUserGroup does not exist. Run usp_CreateTable_tUserGroup first.', 1;

    IF COL_LENGTH('dbo.tProjectRef', 'StatusID') IS NULL
    BEGIN
        ALTER TABLE dbo.tProjectRef
            ADD StatusID INT NOT NULL CONSTRAINT DF_tProjectRef_StatusID DEFAULT (1);
    END;

    IF NOT EXISTS (
        SELECT 1 FROM sys.foreign_keys
         WHERE name = N'FK_tProjectRef_StatusID'
           AND parent_object_id = OBJECT_ID(N'dbo.tProjectRef')
    )
    BEGIN
        ALTER TABLE dbo.tProjectRef
            ADD CONSTRAINT FK_tProjectRef_StatusID FOREIGN KEY (StatusID) REFERENCES dbo.tStatus (StatusID);
    END;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tProjectRef_StatusID' AND object_id = OBJECT_ID(N'dbo.tProjectRef'))
        CREATE INDEX IX_tProjectRef_StatusID ON dbo.tProjectRef (StatusID);

    IF COL_LENGTH('dbo.tProjectRef', 'UserGroupID') IS NULL
    BEGIN
        ALTER TABLE dbo.tProjectRef
            ADD UserGroupID INT NULL;
    END;

    IF NOT EXISTS (
        SELECT 1 FROM sys.foreign_keys
         WHERE name = N'FK_tProjectRef_UserGroupID'
           AND parent_object_id = OBJECT_ID(N'dbo.tProjectRef')
    )
    BEGIN
        ALTER TABLE dbo.tProjectRef
            ADD CONSTRAINT FK_tProjectRef_UserGroupID FOREIGN KEY (UserGroupID) REFERENCES dbo.tUserGroup (UserGroupID);
    END;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tProjectRef_UserGroupID' AND object_id = OBJECT_ID(N'dbo.tProjectRef'))
        CREATE INDEX IX_tProjectRef_UserGroupID ON dbo.tProjectRef (UserGroupID);
END;
GO
/*
  Relaxes BusinessUnitID / BusinessClassID / ClientTypeID on an existing dbo.tClientRef to allow NULL,
  so a client can be partially classified (set one field, leave the rest NULL until filled in later).
  Idempotent.
*/
CREATE OR ALTER PROCEDURE usp_AlterTable_tClientRef_NullableClassification
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID('dbo.tClientRef', 'U') IS NULL
        THROW 50001, 'dbo.tClientRef does not exist. Run usp_CreateTable_tClientRef first.', 1;

    IF COL_LENGTH('dbo.tClientRef', 'BusinessUnitID') IS NOT NULL
        AND COLUMNPROPERTY(OBJECT_ID('dbo.tClientRef'), 'BusinessUnitID', 'AllowsNull') = 0
        ALTER TABLE dbo.tClientRef ALTER COLUMN BusinessUnitID INT NULL;

    IF COL_LENGTH('dbo.tClientRef', 'BusinessClassID') IS NOT NULL
        AND COLUMNPROPERTY(OBJECT_ID('dbo.tClientRef'), 'BusinessClassID', 'AllowsNull') = 0
        ALTER TABLE dbo.tClientRef ALTER COLUMN BusinessClassID INT NULL;

    IF COL_LENGTH('dbo.tClientRef', 'ClientTypeID') IS NOT NULL
        AND COLUMNPROPERTY(OBJECT_ID('dbo.tClientRef'), 'ClientTypeID', 'AllowsNull') = 0
        ALTER TABLE dbo.tClientRef ALTER COLUMN ClientTypeID INT NULL;
END;
GO
CREATE OR ALTER PROCEDURE usp_ClientRef_Create
    @ClientID                     INT,
    @BusinessUnitID               INT = NULL,
    @BusinessClassID              INT = NULL,
    @ClientTypeID                 INT = NULL,
    @ClientTypeRefID              INT = NULL,
    @StatusID                     INT = 1,
    @ActingUserID                INT,
    @NewClientRefID              INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @StatusID = COALESCE(@StatusID, 1);

    BEGIN TRY
        BEGIN TRAN;

        INSERT INTO dbo.tClientRef (ClientID, BusinessUnitID, BusinessClassID, ClientTypeID, ClientTypeRefID, StatusID)
        VALUES (@ClientID, @BusinessUnitID, @BusinessClassID, @ClientTypeID, @ClientTypeRefID, @StatusID);

        SET @NewClientRefID = SCOPE_IDENTITY();

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'ClientRef',
            @EntityID   = @NewClientRefID,
            @UserID     = @ActingUserID,
            @Action     = 'Insert';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE usp_ClientRef_Get
    @ClientRefID INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT * FROM dbo.tClientRef WHERE ClientRefID = @ClientRefID;
END;
GO
CREATE OR ALTER PROCEDURE usp_ClientRef_List
    @ActiveOnly BIT = 1
AS
BEGIN
    SET NOCOUNT ON;
    SELECT *
      FROM dbo.tClientRef
     WHERE (@ActiveOnly = 0 OR IsActive = 1);
END;
GO
CREATE OR ALTER PROCEDURE usp_ClientRef_Update
    @ClientRefID                  INT,
    @ClientID                     INT = NULL,
    @BusinessUnitID               INT = NULL,
    @BusinessClassID              INT = NULL,
    @ClientTypeID                 INT = NULL,
    @ClientTypeRefID              INT = NULL,
    @StatusID                     INT = NULL,
    @ActingUserID                INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.tClientRef WHERE ClientRefID = @ClientRefID)
        THROW 50001, 'tClientRef not found.', 1;

    BEGIN TRY
        BEGIN TRAN;

        UPDATE dbo.tClientRef
           SET
        ClientID = COALESCE(@ClientID, ClientID),
        BusinessUnitID = COALESCE(@BusinessUnitID, BusinessUnitID),
        BusinessClassID = COALESCE(@BusinessClassID, BusinessClassID),
        ClientTypeID = COALESCE(@ClientTypeID, ClientTypeID),
        ClientTypeRefID = COALESCE(@ClientTypeRefID, ClientTypeRefID),
        StatusID = COALESCE(@StatusID, StatusID),
        ModifiedDate = SYSUTCDATETIME()
         WHERE ClientRefID = @ClientRefID;

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'ClientRef',
            @EntityID   = @ClientRefID,
            @UserID     = @ActingUserID,
            @Action     = 'Update';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE usp_ClientRef_SoftDelete
    @ClientRefID INT,
    @ActingUserID INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.tClientRef WHERE ClientRefID = @ClientRefID AND IsActive = 1)
        THROW 50001, 'tClientRef not found or already inactive.', 1;

    BEGIN TRY
        BEGIN TRAN;

        UPDATE dbo.tClientRef
           SET IsActive = 0,
            ModifiedDate = SYSUTCDATETIME()
         WHERE ClientRefID = @ClientRefID;

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'ClientRef',
            @EntityID   = @ClientRefID,
            @UserID     = @ActingUserID,
            @Action     = 'SoftDelete';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO

--tProjectRef
CREATE OR ALTER PROCEDURE usp_CreateTable_tProjectRef
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID('dbo.tProjectRef', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.tProjectRef (
            ProjectRefID INT IDENTITY(1,1) NOT NULL,
        ProjectID                    INT                  NOT NULL,
        ClientRefID                  INT                  NOT NULL,
        ProjectTypeID                INT                  NOT NULL,
        CreatedDate                  DATETIME2(0)         NOT NULL CONSTRAINT DF_tProjectRef_CreatedDate  DEFAULT (SYSUTCDATETIME()),
        ModifiedDate                 DATETIME2(0)         NOT NULL CONSTRAINT DF_tProjectRef_ModifiedDate DEFAULT (SYSUTCDATETIME()),
        IsActive                     BIT                  NOT NULL CONSTRAINT DF_tProjectRef_IsActive DEFAULT (1),
        CONSTRAINT PK_tProjectRef PRIMARY KEY CLUSTERED (ProjectRefID),
        CONSTRAINT FK_tProjectRef_ProjectID FOREIGN KEY (ProjectID) REFERENCES dbo.tProject (ProjectID),
        CONSTRAINT FK_tProjectRef_ClientRefID FOREIGN KEY (ClientRefID) REFERENCES dbo.tClientRef (ClientRefID),
        CONSTRAINT FK_tProjectRef_ProjectTypeID FOREIGN KEY (ProjectTypeID) REFERENCES dbo.tProjectType (ProjectTypeID),
        );

        CREATE INDEX IX_tProjectRef_ProjectID ON dbo.tProjectRef (ProjectID);
        CREATE INDEX IX_tProjectRef_ClientRefID ON dbo.tProjectRef (ClientRefID);
        CREATE INDEX IX_tProjectRef_ProjectTypeID ON dbo.tProjectRef (ProjectTypeID);
    END;
END;
GO
CREATE OR ALTER PROCEDURE usp_ProjectRef_Create
    @ProjectID                    INT,
    @ClientRefID                  INT,
    @ProjectTypeID                INT,
    @ActingUserID                INT,
    @NewProjectRefID             INT OUTPUT,
    @StatusID                     INT = 1,
    @UserGroupID                  INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @StatusID = COALESCE(NULLIF(@StatusID, 0), 1);

    BEGIN TRY
        BEGIN TRAN;

        INSERT INTO dbo.tProjectRef (ProjectID, ClientRefID, ProjectTypeID, StatusID, UserGroupID)
        VALUES (@ProjectID, @ClientRefID, @ProjectTypeID, @StatusID, @UserGroupID);

        SET @NewProjectRefID = SCOPE_IDENTITY();

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'ProjectRef',
            @EntityID   = @NewProjectRefID,
            @UserID     = @ActingUserID,
            @Action     = 'Insert';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE usp_ProjectRef_Get
    @ProjectRefID INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT * FROM dbo.tProjectRef WHERE ProjectRefID = @ProjectRefID;
END;
GO
CREATE OR ALTER PROCEDURE usp_ProjectRef_List
    @ActiveOnly BIT = 1
AS
BEGIN
    SET NOCOUNT ON;
    SELECT *
      FROM dbo.tProjectRef
     WHERE (@ActiveOnly = 0 OR IsActive = 1);
END;
GO
CREATE OR ALTER PROCEDURE usp_ProjectRef_Update
    @ProjectRefID                 INT,
    @ProjectID                    INT = NULL,
    @ClientRefID                  INT = NULL,
    @ProjectTypeID                INT = NULL,
    @StatusID                     INT = NULL,
    @UserGroupID                  INT = NULL,
    @ActingUserID                INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.tProjectRef WHERE ProjectRefID = @ProjectRefID)
        THROW 50001, 'tProjectRef not found.', 1;

    BEGIN TRY
        BEGIN TRAN;

        UPDATE dbo.tProjectRef
           SET
        ProjectID = COALESCE(@ProjectID, ProjectID),
        ClientRefID = COALESCE(@ClientRefID, ClientRefID),
        ProjectTypeID = COALESCE(@ProjectTypeID, ProjectTypeID),
        StatusID = COALESCE(NULLIF(@StatusID, 0), StatusID),
        UserGroupID = CASE
            WHEN @UserGroupID IS NULL OR @UserGroupID <= 0 THEN UserGroupID
            ELSE @UserGroupID
        END,
        ModifiedDate = SYSUTCDATETIME()
         WHERE ProjectRefID = @ProjectRefID;

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'ProjectRef',
            @EntityID   = @ProjectRefID,
            @UserID     = @ActingUserID,
            @Action     = 'Update';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE usp_ProjectRef_SoftDelete
    @ProjectRefID INT,
    @ActingUserID INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.tProjectRef WHERE ProjectRefID = @ProjectRefID AND IsActive = 1)
        THROW 50001, 'tProjectRef not found or already inactive.', 1;

    BEGIN TRY
        BEGIN TRAN;

        UPDATE dbo.tProjectRef
           SET IsActive = 0,
            ModifiedDate = SYSUTCDATETIME()
         WHERE ProjectRefID = @ProjectRefID;

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'ProjectRef',
            @EntityID   = @ProjectRefID,
            @UserID     = @ActingUserID,
            @Action     = 'SoftDelete';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO

--tServiceRef
CREATE OR ALTER PROCEDURE usp_CreateTable_tServiceRef
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID('dbo.tServiceRef', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.tServiceRef (
            ServiceRefID INT IDENTITY(1,1) NOT NULL,
        ServiceCategoryID            INT                  NOT NULL,
        ServiceGroupID               INT                  NULL,
        ServiceID                    INT                  NOT NULL,
        CreatedDate                  DATETIME2(0)         NOT NULL CONSTRAINT DF_tServiceRef_CreatedDate  DEFAULT (SYSUTCDATETIME()),
        ModifiedDate                 DATETIME2(0)         NOT NULL CONSTRAINT DF_tServiceRef_ModifiedDate DEFAULT (SYSUTCDATETIME()),
        IsActive                     BIT                  NOT NULL CONSTRAINT DF_tServiceRef_IsActive DEFAULT (1),
        CONSTRAINT PK_tServiceRef PRIMARY KEY CLUSTERED (ServiceRefID),
        CONSTRAINT FK_tServiceRef_ServiceCategoryID FOREIGN KEY (ServiceCategoryID) REFERENCES dbo.tServiceCategory (ServiceCategoryID),
        CONSTRAINT FK_tServiceRef_ServiceGroupID FOREIGN KEY (ServiceGroupID) REFERENCES dbo.tServiceGroup (ServiceGroupID),
        CONSTRAINT FK_tServiceRef_ServiceID FOREIGN KEY (ServiceID) REFERENCES dbo.tService (ServiceID)
        );

        CREATE INDEX IX_tServiceRef_ServiceCategoryID ON dbo.tServiceRef (ServiceCategoryID);
        CREATE INDEX IX_tServiceRef_ServiceGroupID ON dbo.tServiceRef (ServiceGroupID);
        CREATE INDEX IX_tServiceRef_ServiceID ON dbo.tServiceRef (ServiceID);
    END;
END;
GO
CREATE OR ALTER PROCEDURE usp_ServiceRef_Create
    @ServiceCategoryID            INT,
    @ServiceGroupID               INT = NULL,
    @ServiceID                    INT,
    @ActingUserID                INT,
    @NewServiceRefID             INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRAN;

        INSERT INTO dbo.tServiceRef (ServiceCategoryID, ServiceGroupID, ServiceID)
        VALUES (@ServiceCategoryID, @ServiceGroupID, @ServiceID);

        SET @NewServiceRefID = SCOPE_IDENTITY();

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'ServiceRef',
            @EntityID   = @NewServiceRefID,
            @UserID     = @ActingUserID,
            @Action     = 'Insert';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE usp_ServiceRef_Get
    @ServiceRefID INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT * FROM dbo.tServiceRef WHERE ServiceRefID = @ServiceRefID;
END;
GO
CREATE OR ALTER PROCEDURE usp_ServiceRef_List
    @ActiveOnly BIT = 1
AS
BEGIN
    SET NOCOUNT ON;
    SELECT *
      FROM dbo.tServiceRef
     WHERE (@ActiveOnly = 0 OR IsActive = 1);
END;
GO
CREATE OR ALTER PROCEDURE usp_ServiceRef_Update
    @ServiceRefID                 INT,
    @ServiceCategoryID            INT = NULL,
    @ServiceGroupID               INT = NULL,
    @ServiceID                    INT = NULL,
    @ActingUserID                INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.tServiceRef WHERE ServiceRefID = @ServiceRefID)
        THROW 50001, 'tServiceRef not found.', 1;

    BEGIN TRY
        BEGIN TRAN;

        UPDATE dbo.tServiceRef
           SET
        ServiceCategoryID = COALESCE(@ServiceCategoryID, ServiceCategoryID),
        ServiceGroupID = COALESCE(@ServiceGroupID, ServiceGroupID),
        ServiceID = COALESCE(@ServiceID, ServiceID),
        ModifiedDate = SYSUTCDATETIME()
         WHERE ServiceRefID = @ServiceRefID;

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'ServiceRef',
            @EntityID   = @ServiceRefID,
            @UserID     = @ActingUserID,
            @Action     = 'Update';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE usp_ServiceRef_SoftDelete
    @ServiceRefID INT,
    @ActingUserID INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.tServiceRef WHERE ServiceRefID = @ServiceRefID AND IsActive = 1)
        THROW 50001, 'tServiceRef not found or already inactive.', 1;

    BEGIN TRY
        BEGIN TRAN;

        UPDATE dbo.tServiceRef
           SET IsActive = 0,
            ModifiedDate = SYSUTCDATETIME()
         WHERE ServiceRefID = @ServiceRefID;

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'ServiceRef',
            @EntityID   = @ServiceRefID,
            @UserID     = @ActingUserID,
            @Action     = 'SoftDelete';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO

--tProjectNote
CREATE OR ALTER PROCEDURE usp_CreateTable_tProjectNote
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID('dbo.tProjectNote', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.tProjectNote (
            NoteID INT IDENTITY(1,1) NOT NULL,
        ProjectRefID                 INT                  NOT NULL,
        NoteDate                     DATETIME2(0)         NOT NULL,
        NoteType                     VARCHAR(30)          NOT NULL,
        NoteText                     NVARCHAR(MAX)        NOT NULL,
        IsPinned                     BIT                  NOT NULL,
        CreatedByUserID              INT                  NULL,
        CalendarItemID               INT                  NULL,
        CalendarItemTitle            NVARCHAR(200)        NULL,
        CreatedDate                  DATETIME2(0)         NOT NULL CONSTRAINT DF_tProjectNote_CreatedDate  DEFAULT (SYSUTCDATETIME()),
        ModifiedDate                 DATETIME2(0)         NOT NULL CONSTRAINT DF_tProjectNote_ModifiedDate DEFAULT (SYSUTCDATETIME()),
        IsActive                     BIT                  NOT NULL CONSTRAINT DF_tProjectNote_IsActive DEFAULT (1),
        CONSTRAINT PK_tProjectNote PRIMARY KEY CLUSTERED (NoteID),
        CONSTRAINT FK_tProjectNote_ProjectRefID FOREIGN KEY (ProjectRefID) REFERENCES dbo.tProjectRef (ProjectRefID) ON DELETE CASCADE
        );

        CREATE INDEX IX_tProjectNote_ProjectRefID ON dbo.tProjectNote (ProjectRefID);
        CREATE NONCLUSTERED INDEX IX_tProjectNote_CalendarItemID
            ON dbo.tProjectNote (CalendarItemID)
            WHERE CalendarItemID IS NOT NULL;
    END;
END;
GO
CREATE OR ALTER PROCEDURE usp_ProjectNote_Create
    @ProjectRefID                 INT,
    @NoteDate                     DATETIME2(0),
    @NoteType                     VARCHAR(30),
    @NoteText                     NVARCHAR(MAX),
    @IsPinned                     BIT,
    @ActingUserID                 INT,
    @CreatedByUserID              INT = NULL,
    @CalendarItemID               INT = NULL,
    @CalendarItemTitle            NVARCHAR(200) = NULL,
    @NewNoteID                    INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRAN;

        INSERT INTO dbo.tProjectNote (
            ProjectRefID,
            NoteDate,
            NoteType,
            NoteText,
            IsPinned,
            CreatedByUserID,
            CalendarItemID,
            CalendarItemTitle
        )
        VALUES (
            @ProjectRefID,
            @NoteDate,
            @NoteType,
            @NoteText,
            @IsPinned,
            @CreatedByUserID,
            @CalendarItemID,
            @CalendarItemTitle
        );

        SET @NewNoteID = SCOPE_IDENTITY();

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'ProjectNote',
            @EntityID   = @NewNoteID,
            @UserID     = @ActingUserID,
            @Action     = 'Insert';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE usp_ProjectNote_Get
    @NoteID INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT * FROM dbo.tProjectNote WHERE NoteID = @NoteID;
END;
GO
CREATE OR ALTER PROCEDURE usp_ProjectNote_List
    @ActiveOnly BIT = 1
AS
BEGIN
    SET NOCOUNT ON;
    SELECT *
      FROM dbo.tProjectNote
     WHERE (@ActiveOnly = 0 OR IsActive = 1);
END;
GO
CREATE OR ALTER PROCEDURE usp_ProjectNote_Update
    @NoteID                       INT,
    @ProjectRefID                 INT = NULL,
    @NoteDate                     DATETIME2(0) = NULL,
    @NoteType                     VARCHAR(30) = NULL,
    @NoteText                     NVARCHAR(MAX) = NULL,
    @IsPinned                     BIT = NULL,
    @ActingUserID                INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.tProjectNote WHERE NoteID = @NoteID)
        THROW 50001, 'tProjectNote not found.', 1;

    BEGIN TRY
        BEGIN TRAN;

        UPDATE dbo.tProjectNote
           SET
        ProjectRefID = COALESCE(@ProjectRefID, ProjectRefID),
        NoteDate = COALESCE(@NoteDate, NoteDate),
        NoteType = COALESCE(@NoteType, NoteType),
        NoteText = COALESCE(@NoteText, NoteText),
        IsPinned = COALESCE(@IsPinned, IsPinned),
        ModifiedDate = SYSUTCDATETIME()
         WHERE NoteID = @NoteID;

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'ProjectNote',
            @EntityID   = @NoteID,
            @UserID     = @ActingUserID,
            @Action     = 'Update';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE usp_ProjectNote_SoftDelete
    @NoteID INT,
    @ActingUserID INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.tProjectNote WHERE NoteID = @NoteID AND IsActive = 1)
        THROW 50001, 'tProjectNote not found or already inactive.', 1;

    BEGIN TRY
        BEGIN TRAN;

        UPDATE dbo.tProjectNote
           SET IsActive = 0,
            ModifiedDate = SYSUTCDATETIME()
         WHERE NoteID = @NoteID;

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'ProjectNote',
            @EntityID   = @NoteID,
            @UserID     = @ActingUserID,
            @Action     = 'SoftDelete';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO

--tProjectRevenue
CREATE OR ALTER PROCEDURE usp_CreateTable_tProjectRevenue
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID('dbo.tProjectRevenue', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.tProjectRevenue (
            RevenueID INT IDENTITY(1,1) NOT NULL,
        ProjectRefID                 INT                  NOT NULL,
        EffectiveDate                DATE                 NOT NULL,
        Revenue                      DECIMAL(18,2)        NOT NULL,
        ChangeReason                 VARCHAR(255)         NULL,
        RecordedBy                   INT                  NOT NULL,
        CreatedDate                  DATETIME2(0)         NOT NULL CONSTRAINT DF_tProjectRevenue_CreatedDate  DEFAULT (SYSUTCDATETIME()),
        ModifiedDate                 DATETIME2(0)         NOT NULL CONSTRAINT DF_tProjectRevenue_ModifiedDate DEFAULT (SYSUTCDATETIME()),
        IsActive                     BIT                  NOT NULL CONSTRAINT DF_tProjectRevenue_IsActive DEFAULT (1),
        CONSTRAINT PK_tProjectRevenue PRIMARY KEY CLUSTERED (RevenueID),
        CONSTRAINT FK_tProjectRevenue_ProjectRefID FOREIGN KEY (ProjectRefID) REFERENCES dbo.tProjectRef (ProjectRefID) ON DELETE CASCADE,
        CONSTRAINT FK_tProjectRevenue_RecordedBy FOREIGN KEY (RecordedBy) REFERENCES dbo.tUser (UserID)
        );

        CREATE INDEX IX_tProjectRevenue_ProjectRefID ON dbo.tProjectRevenue (ProjectRefID);
        CREATE INDEX IX_tProjectRevenue_RecordedBy ON dbo.tProjectRevenue (RecordedBy);
    END;
END;
GO
CREATE OR ALTER PROCEDURE usp_ProjectRevenue_Create
    @ProjectRefID                 INT,
    @EffectiveDate                DATE,
    @Revenue                      DECIMAL(18,2),
    @ChangeReason                 VARCHAR(255) = NULL,
    @RecordedBy                   INT,
    @ActingUserID                INT,
    @NewRevenueID                INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRAN;

        INSERT INTO dbo.tProjectRevenue (ProjectRefID, EffectiveDate, Revenue, ChangeReason, RecordedBy)
        VALUES (@ProjectRefID, @EffectiveDate, @Revenue, @ChangeReason, @RecordedBy);

        SET @NewRevenueID = SCOPE_IDENTITY();

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'ProjectRevenue',
            @EntityID   = @NewRevenueID,
            @UserID     = @ActingUserID,
            @Action     = 'Insert';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE usp_ProjectRevenue_Get
    @RevenueID INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT * FROM dbo.tProjectRevenue WHERE RevenueID = @RevenueID;
END;
GO
CREATE OR ALTER PROCEDURE usp_ProjectRevenue_List
    @ActiveOnly BIT = 1
AS
BEGIN
    SET NOCOUNT ON;
    SELECT *
      FROM dbo.tProjectRevenue
     WHERE (@ActiveOnly = 0 OR IsActive = 1);
END;
GO
CREATE OR ALTER PROCEDURE usp_ProjectRevenue_Update
    @RevenueID                    INT,
    @ProjectRefID                 INT = NULL,
    @EffectiveDate                DATE = NULL,
    @Revenue                      DECIMAL(18,2) = NULL,
    @ChangeReason                 VARCHAR(255) = NULL,
    @RecordedBy                   INT = NULL,
    @ActingUserID                INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.tProjectRevenue WHERE RevenueID = @RevenueID)
        THROW 50001, 'tProjectRevenue not found.', 1;

    BEGIN TRY
        BEGIN TRAN;

        UPDATE dbo.tProjectRevenue
           SET
        ProjectRefID = COALESCE(@ProjectRefID, ProjectRefID),
        EffectiveDate = COALESCE(@EffectiveDate, EffectiveDate),
        Revenue = COALESCE(@Revenue, Revenue),
        ChangeReason = COALESCE(@ChangeReason, ChangeReason),
        RecordedBy = COALESCE(@RecordedBy, RecordedBy),
        ModifiedDate = SYSUTCDATETIME()
         WHERE RevenueID = @RevenueID;

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'ProjectRevenue',
            @EntityID   = @RevenueID,
            @UserID     = @ActingUserID,
            @Action     = 'Update';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE usp_ProjectRevenue_SoftDelete
    @RevenueID INT,
    @ActingUserID INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.tProjectRevenue WHERE RevenueID = @RevenueID AND IsActive = 1)
        THROW 50001, 'tProjectRevenue not found or already inactive.', 1;

    BEGIN TRY
        BEGIN TRAN;

        UPDATE dbo.tProjectRevenue
           SET IsActive = 0,
            ModifiedDate = SYSUTCDATETIME()
         WHERE RevenueID = @RevenueID;

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'ProjectRevenue',
            @EntityID   = @RevenueID,
            @UserID     = @ActingUserID,
            @Action     = 'SoftDelete';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO

--tProjectService
CREATE OR ALTER PROCEDURE usp_CreateTable_tProjectService
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID('dbo.tProjectService', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.tProjectService (
            ProjectServiceID INT IDENTITY(1,1) NOT NULL,
        ProjectRefID                 INT                  NOT NULL,
        ServiceRefID                 INT                  NOT NULL,
        Frequency                    VARCHAR(20)          NULL,
        StartDate                    DATE                 NULL,
        EndDate                      DATE                 NULL,
        CreatedDate                  DATETIME2(0)         NOT NULL CONSTRAINT DF_tProjectService_CreatedDate  DEFAULT (SYSUTCDATETIME()),
        ModifiedDate                 DATETIME2(0)         NOT NULL CONSTRAINT DF_tProjectService_ModifiedDate DEFAULT (SYSUTCDATETIME()),
        IsActive                     BIT                  NOT NULL CONSTRAINT DF_tProjectService_IsActive DEFAULT (1),
        CONSTRAINT PK_tProjectService PRIMARY KEY CLUSTERED (ProjectServiceID),
        CONSTRAINT FK_tProjectService_ProjectRefID FOREIGN KEY (ProjectRefID) REFERENCES dbo.tProjectRef (ProjectRefID) ON DELETE CASCADE,
        CONSTRAINT FK_tProjectService_ServiceRefID FOREIGN KEY (ServiceRefID) REFERENCES dbo.tServiceRef (ServiceRefID)
        );

        CREATE INDEX IX_tProjectService_ProjectRefID ON dbo.tProjectService (ProjectRefID);
        CREATE INDEX IX_tProjectService_ServiceRefID ON dbo.tProjectService (ServiceRefID);
    END;
END;
GO
CREATE OR ALTER PROCEDURE usp_ProjectService_Create
    @ProjectRefID                 INT,
    @ServiceRefID                 INT,
    @Frequency                    VARCHAR(20) = NULL,
    @StartDate                    DATE = NULL,
    @EndDate                      DATE = NULL,
    @ActingUserID                INT,
    @NewProjectServiceID         INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRAN;

        INSERT INTO dbo.tProjectService (ProjectRefID, ServiceRefID, Frequency, StartDate, EndDate)
        VALUES (@ProjectRefID, @ServiceRefID, @Frequency, @StartDate, @EndDate);

        SET @NewProjectServiceID = SCOPE_IDENTITY();

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'ProjectService',
            @EntityID   = @NewProjectServiceID,
            @UserID     = @ActingUserID,
            @Action     = 'Insert';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE usp_ProjectService_Get
    @ProjectServiceID INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT * FROM dbo.tProjectService WHERE ProjectServiceID = @ProjectServiceID;
END;
GO
CREATE OR ALTER PROCEDURE usp_ProjectService_List
    @ActiveOnly BIT = 1
AS
BEGIN
    SET NOCOUNT ON;
    SELECT *
      FROM dbo.tProjectService
     WHERE (@ActiveOnly = 0 OR IsActive = 1);
END;
GO
CREATE OR ALTER PROCEDURE usp_ProjectService_Update
    @ProjectServiceID             INT,
    @ProjectRefID                 INT = NULL,
    @ServiceRefID                 INT = NULL,
    @Frequency                    VARCHAR(20) = NULL,
    @StartDate                    DATE = NULL,
    @EndDate                      DATE = NULL,
    @ActingUserID                INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.tProjectService WHERE ProjectServiceID = @ProjectServiceID)
        THROW 50001, 'tProjectService not found.', 1;

    BEGIN TRY
        BEGIN TRAN;

        UPDATE dbo.tProjectService
           SET
        ProjectRefID = COALESCE(@ProjectRefID, ProjectRefID),
        ServiceRefID = COALESCE(@ServiceRefID, ServiceRefID),
        Frequency = COALESCE(@Frequency, Frequency),
        StartDate = COALESCE(@StartDate, StartDate),
        EndDate = COALESCE(@EndDate, EndDate),
        ModifiedDate = SYSUTCDATETIME()
         WHERE ProjectServiceID = @ProjectServiceID;

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'ProjectService',
            @EntityID   = @ProjectServiceID,
            @UserID     = @ActingUserID,
            @Action     = 'Update';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE usp_ProjectService_SoftDelete
    @ProjectServiceID INT,
    @ActingUserID INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.tProjectService WHERE ProjectServiceID = @ProjectServiceID AND IsActive = 1)
        THROW 50001, 'tProjectService not found or already inactive.', 1;

    BEGIN TRY
        BEGIN TRAN;

        UPDATE dbo.tProjectService
           SET IsActive = 0,
            ModifiedDate = SYSUTCDATETIME()
         WHERE ProjectServiceID = @ProjectServiceID;

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'ProjectService',
            @EntityID   = @ProjectServiceID,
            @UserID     = @ActingUserID,
            @Action     = 'SoftDelete';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO

--tProjectUser
CREATE OR ALTER PROCEDURE usp_CreateTable_tProjectUser
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID('dbo.tProjectUser', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.tProjectUser (
            ProjectUserID INT IDENTITY(1,1) NOT NULL,
        ProjectRefID                 INT                  NOT NULL,
        UserID                       INT                  NOT NULL,
        UserRoleID                   INT                  NULL,
        StartDate                    DATE                 NULL,
        EndDate                      DATE                 NULL,
        CreatedDate                  DATETIME2(0)         NOT NULL CONSTRAINT DF_tProjectUser_CreatedDate  DEFAULT (SYSUTCDATETIME()),
        ModifiedDate                 DATETIME2(0)         NOT NULL CONSTRAINT DF_tProjectUser_ModifiedDate DEFAULT (SYSUTCDATETIME()),
        IsActive                     BIT                  NOT NULL CONSTRAINT DF_tProjectUser_IsActive DEFAULT (1),
        CONSTRAINT PK_tProjectUser PRIMARY KEY CLUSTERED (ProjectUserID),
        CONSTRAINT FK_tProjectUser_ProjectRefID FOREIGN KEY (ProjectRefID) REFERENCES dbo.tProjectRef (ProjectRefID) ON DELETE CASCADE,
        CONSTRAINT FK_tProjectUser_UserID FOREIGN KEY (UserID) REFERENCES dbo.tUser (UserID) ON DELETE CASCADE,
        CONSTRAINT FK_tProjectUser_UserRoleID FOREIGN KEY (UserRoleID) REFERENCES dbo.tUserRole (UserRoleID)
        );

        CREATE INDEX IX_tProjectUser_ProjectRefID ON dbo.tProjectUser (ProjectRefID);
        CREATE INDEX IX_tProjectUser_UserID ON dbo.tProjectUser (UserID);
        CREATE INDEX IX_tProjectUser_UserRoleID ON dbo.tProjectUser (UserRoleID);
    END;
END;
GO
CREATE OR ALTER PROCEDURE usp_ProjectUser_Create
    @ProjectRefID                 INT,
    @UserID                       INT,
    @UserRoleID                   INT = NULL,
    @StartDate                    DATE = NULL,
    @EndDate                      DATE = NULL,
    @ActingUserID                INT,
    @NewProjectUserID            INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRAN;

        INSERT INTO dbo.tProjectUser (ProjectRefID, UserID, UserRoleID, StartDate, EndDate)
        VALUES (@ProjectRefID, @UserID, @UserRoleID, @StartDate, @EndDate);

        SET @NewProjectUserID = SCOPE_IDENTITY();

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'ProjectUser',
            @EntityID   = @NewProjectUserID,
            @UserID     = @ActingUserID,
            @Action     = 'Insert';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE usp_ProjectUser_Get
    @ProjectUserID INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT * FROM dbo.tProjectUser WHERE ProjectUserID = @ProjectUserID;
END;
GO
CREATE OR ALTER PROCEDURE usp_ProjectUser_List
    @ActiveOnly BIT = 1
AS
BEGIN
    SET NOCOUNT ON;
    SELECT *
      FROM dbo.tProjectUser
     WHERE (@ActiveOnly = 0 OR IsActive = 1);
END;
GO
CREATE OR ALTER PROCEDURE usp_ProjectUser_Update
    @ProjectUserID                INT,
    @ProjectRefID                 INT = NULL,
    @UserID                       INT = NULL,
    @UserRoleID                   INT = NULL,
    @StartDate                    DATE = NULL,
    @EndDate                      DATE = NULL,
    @ActingUserID                INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.tProjectUser WHERE ProjectUserID = @ProjectUserID)
        THROW 50001, 'tProjectUser not found.', 1;

    BEGIN TRY
        BEGIN TRAN;

        UPDATE dbo.tProjectUser
           SET
        ProjectRefID = COALESCE(@ProjectRefID, ProjectRefID),
        UserID = COALESCE(@UserID, UserID),
        UserRoleID = COALESCE(@UserRoleID, UserRoleID),
        StartDate = COALESCE(@StartDate, StartDate),
        EndDate = COALESCE(@EndDate, EndDate),
        ModifiedDate = SYSUTCDATETIME()
         WHERE ProjectUserID = @ProjectUserID;

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'ProjectUser',
            @EntityID   = @ProjectUserID,
            @UserID     = @ActingUserID,
            @Action     = 'Update';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE usp_ProjectUser_SoftDelete
    @ProjectUserID INT,
    @ActingUserID INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.tProjectUser WHERE ProjectUserID = @ProjectUserID AND IsActive = 1)
        THROW 50001, 'tProjectUser not found or already inactive.', 1;

    BEGIN TRY
        BEGIN TRAN;

        UPDATE dbo.tProjectUser
           SET IsActive = 0,
            ModifiedDate = SYSUTCDATETIME()
         WHERE ProjectUserID = @ProjectUserID;

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'ProjectUser',
            @EntityID   = @ProjectUserID,
            @UserID     = @ActingUserID,
            @Action     = 'SoftDelete';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO

--tUserGroupRef
CREATE OR ALTER PROCEDURE usp_CreateTable_tUserGroupRef
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID('dbo.tUserGroupRef', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.tUserGroupRef (
            UserGroupID  INT          NOT NULL,
            UserID       INT          NOT NULL,
            UserRoleID   INT          NULL,
            JoinedDate   DATETIME2(0) NOT NULL,
            CONSTRAINT PK_tUserGroupRef PRIMARY KEY CLUSTERED (UserGroupID, UserID),
            CONSTRAINT FK_tUserGroupRef_UserGroupID FOREIGN KEY (UserGroupID) REFERENCES dbo.tUserGroup (UserGroupID),
            CONSTRAINT FK_tUserGroupRef_UserID      FOREIGN KEY (UserID)      REFERENCES dbo.tUser (UserID),
            CONSTRAINT FK_tUserGroupRef_UserRoleID  FOREIGN KEY (UserRoleID)  REFERENCES dbo.tUserRole (UserRoleID)
        );

        CREATE INDEX IX_tUserGroupRef_UserID ON dbo.tUserGroupRef (UserID);
    END;
END;
GO
CREATE OR ALTER PROCEDURE usp_UserGroupRef_Add
    @UserGroupID  INT,
    @UserID       INT,
    @UserRoleID   INT = NULL,
    @JoinedDate   DATETIME2(0),
    @ActingUserID INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    -- Idempotent: skip if pair already exists
    IF EXISTS (SELECT 1 FROM dbo.tUserGroupRef WHERE UserGroupID = @UserGroupID AND UserID = @UserID)
        RETURN 0;

    BEGIN TRY
        BEGIN TRAN;

        INSERT INTO dbo.tUserGroupRef (UserGroupID, UserID, UserRoleID, JoinedDate)
        VALUES (@UserGroupID, @UserID, @UserRoleID, @JoinedDate);

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'UserGroupRef',
            @EntityID   = @UserGroupID,
            @UserID     = @ActingUserID,
            @Action     = 'Add';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE usp_UserGroupRef_Remove
    @UserGroupID  INT,
    @UserID       INT,
    @ActingUserID INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRAN;

        DELETE FROM dbo.tUserGroupRef
         WHERE UserGroupID = @UserGroupID AND UserID = @UserID;

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'UserGroupRef',
            @EntityID   = @UserGroupID,
            @UserID     = @ActingUserID,
            @Action     = 'Remove';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE usp_UserGroupRef_List
    @UserGroupID INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT * FROM dbo.tUserGroupRef WHERE UserGroupID = @UserGroupID;
END;
GO

--tBusinessClassTag
CREATE OR ALTER PROCEDURE usp_CreateTable_tBusinessClassTag
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID('dbo.tBusinessClassTag', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.tBusinessClassTag (
        BusinessClassID      INT NOT NULL,
        TagID                INT NOT NULL,
        CONSTRAINT PK_tBusinessClassTag PRIMARY KEY CLUSTERED (BusinessClassID, TagID),
        CONSTRAINT FK_tBusinessClassTag_BusinessClassID FOREIGN KEY (BusinessClassID) REFERENCES dbo.tBusinessClass (BusinessClassID),
        CONSTRAINT FK_tBusinessClassTag_TagID FOREIGN KEY (TagID) REFERENCES dbo.tTag (TagID)
        );

        CREATE INDEX IX_tBusinessClassTag_TagID ON dbo.tBusinessClassTag (TagID);
    END;
END;
GO
CREATE OR ALTER PROCEDURE usp_BusinessClassTag_Add
    @BusinessClassID      INT,
    @TagID                INT,
    @ActingUserID         INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    -- Idempotent: skip if pair already exists
    IF EXISTS (SELECT 1 FROM dbo.tBusinessClassTag WHERE BusinessClassID = @BusinessClassID AND TagID = @TagID)
        RETURN 0;

    BEGIN TRY
        BEGIN TRAN;

        INSERT INTO dbo.tBusinessClassTag (BusinessClassID, TagID)
        VALUES (@BusinessClassID, @TagID);

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'BusinessClassTag',
            @EntityID   = @BusinessClassID,
            @UserID     = @ActingUserID,
            @Action     = 'Add';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE usp_BusinessClassTag_Remove
    @BusinessClassID      INT,
    @TagID                INT,
    @ActingUserID         INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRAN;

        DELETE FROM dbo.tBusinessClassTag WHERE BusinessClassID = @BusinessClassID AND TagID = @TagID;

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'BusinessClassTag',
            @EntityID   = @BusinessClassID,
            @UserID     = @ActingUserID,
            @Action     = 'Remove';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE usp_BusinessClassTag_List
    @BusinessClassID INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT * FROM dbo.tBusinessClassTag WHERE BusinessClassID = @BusinessClassID;
END;
GO

--tUserRoleTag
CREATE OR ALTER PROCEDURE usp_CreateTable_tUserRoleTag
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID('dbo.tUserRoleTag', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.tUserRoleTag (
        UserRoleID           INT NOT NULL,
        TagID                INT NOT NULL,
        CONSTRAINT PK_tUserRoleTag PRIMARY KEY CLUSTERED (UserRoleID, TagID),
        CONSTRAINT FK_tUserRoleTag_UserRoleID FOREIGN KEY (UserRoleID) REFERENCES dbo.tUserRole (UserRoleID),
        CONSTRAINT FK_tUserRoleTag_TagID FOREIGN KEY (TagID) REFERENCES dbo.tTag (TagID)
        );

        CREATE INDEX IX_tUserRoleTag_TagID ON dbo.tUserRoleTag (TagID);
    END;
END;
GO
CREATE OR ALTER PROCEDURE usp_UserRoleTag_Add
    @UserRoleID           INT,
    @TagID                INT,
    @ActingUserID         INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    -- Idempotent: skip if pair already exists
    IF EXISTS (SELECT 1 FROM dbo.tUserRoleTag WHERE UserRoleID = @UserRoleID AND TagID = @TagID)
        RETURN 0;

    BEGIN TRY
        BEGIN TRAN;

        INSERT INTO dbo.tUserRoleTag (UserRoleID, TagID)
        VALUES (@UserRoleID, @TagID);

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'UserRoleTag',
            @EntityID   = @UserRoleID,
            @UserID     = @ActingUserID,
            @Action     = 'Add';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE usp_UserRoleTag_Remove
    @UserRoleID           INT,
    @TagID                INT,
    @ActingUserID         INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRAN;

        DELETE FROM dbo.tUserRoleTag WHERE UserRoleID = @UserRoleID AND TagID = @TagID;

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'UserRoleTag',
            @EntityID   = @UserRoleID,
            @UserID     = @ActingUserID,
            @Action     = 'Remove';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE usp_UserRoleTag_List
    @UserRoleID INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT * FROM dbo.tUserRoleTag WHERE UserRoleID = @UserRoleID;
END;
GO

--tClientTag
CREATE OR ALTER PROCEDURE usp_CreateTable_tClientTag
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID('dbo.tClientTag', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.tClientTag (
        ClientID             INT NOT NULL,
        TagID                INT NOT NULL,
        CONSTRAINT PK_tClientTag PRIMARY KEY CLUSTERED (ClientID, TagID),
        CONSTRAINT FK_tClientTag_ClientID FOREIGN KEY (ClientID) REFERENCES dbo.tClient (ClientID),
        CONSTRAINT FK_tClientTag_TagID FOREIGN KEY (TagID) REFERENCES dbo.tTag (TagID)
        );

        CREATE INDEX IX_tClientTag_TagID ON dbo.tClientTag (TagID);
    END;
END;
GO
CREATE OR ALTER PROCEDURE usp_ClientTag_Add
    @ClientID             INT,
    @TagID                INT,
    @ActingUserID         INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    -- Idempotent: skip if pair already exists
    IF EXISTS (SELECT 1 FROM dbo.tClientTag WHERE ClientID = @ClientID AND TagID = @TagID)
        RETURN 0;

    BEGIN TRY
        BEGIN TRAN;

        INSERT INTO dbo.tClientTag (ClientID, TagID)
        VALUES (@ClientID, @TagID);

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'ClientTag',
            @EntityID   = @ClientID,
            @UserID     = @ActingUserID,
            @Action     = 'Add';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE usp_ClientTag_Remove
    @ClientID             INT,
    @TagID                INT,
    @ActingUserID         INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRAN;

        DELETE FROM dbo.tClientTag WHERE ClientID = @ClientID AND TagID = @TagID;

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'ClientTag',
            @EntityID   = @ClientID,
            @UserID     = @ActingUserID,
            @Action     = 'Remove';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE usp_ClientTag_List
    @ClientID INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT * FROM dbo.tClientTag WHERE ClientID = @ClientID;
END;
GO


CREATE OR ALTER PROCEDURE usp_CreateTable_tProjectTag
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID('dbo.tProjectTag', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.tProjectTag (
        ProjectRefID         INT NOT NULL,
        TagID                INT NOT NULL,
        CONSTRAINT PK_tProjectTag PRIMARY KEY CLUSTERED (ProjectRefID, TagID),
        CONSTRAINT FK_tProjectTag_ProjectRefID FOREIGN KEY (ProjectRefID) REFERENCES dbo.tProjectRef (ProjectRefID),
        CONSTRAINT FK_tProjectTag_TagID FOREIGN KEY (TagID) REFERENCES dbo.tTag (TagID)
        );

        CREATE INDEX IX_tProjectTag_TagID ON dbo.tProjectTag (TagID);
    END;
END;
GO
CREATE OR ALTER PROCEDURE usp_ProjectTag_Add
    @ProjectRefID         INT,
    @TagID                INT,
    @ActingUserID         INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    -- Idempotent: skip if pair already exists
    IF EXISTS (SELECT 1 FROM dbo.tProjectTag WHERE ProjectRefID = @ProjectRefID AND TagID = @TagID)
        RETURN 0;

    BEGIN TRY
        BEGIN TRAN;

        INSERT INTO dbo.tProjectTag (ProjectRefID, TagID)
        VALUES (@ProjectRefID, @TagID);

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'ProjectTag',
            @EntityID   = @ProjectRefID,
            @UserID     = @ActingUserID,
            @Action     = 'Add';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE usp_ProjectTag_Remove
    @ProjectRefID         INT,
    @TagID                INT,
    @ActingUserID         INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRAN;

        DELETE FROM dbo.tProjectTag WHERE ProjectRefID = @ProjectRefID AND TagID = @TagID;

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'ProjectTag',
            @EntityID   = @ProjectRefID,
            @UserID     = @ActingUserID,
            @Action     = 'Remove';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE usp_ProjectTag_List
    @ProjectRefID INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT * FROM dbo.tProjectTag WHERE ProjectRefID = @ProjectRefID;
END;
GO


CREATE OR ALTER PROCEDURE usp_CreateTable_tServiceTag
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID('dbo.tServiceTag', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.tServiceTag (
        ServiceRefID         INT NOT NULL,
        TagID                INT NOT NULL,
        CONSTRAINT PK_tServiceTag PRIMARY KEY CLUSTERED (ServiceRefID, TagID),
        CONSTRAINT FK_tServiceTag_ServiceRefID FOREIGN KEY (ServiceRefID) REFERENCES dbo.tServiceRef (ServiceRefID),
        CONSTRAINT FK_tServiceTag_TagID FOREIGN KEY (TagID) REFERENCES dbo.tTag (TagID)
        );

        CREATE INDEX IX_tServiceTag_TagID ON dbo.tServiceTag (TagID);
    END;
END;
GO
CREATE OR ALTER PROCEDURE usp_ServiceTag_Add
    @ServiceRefID         INT,
    @TagID                INT,
    @ActingUserID         INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    -- Idempotent: skip if pair already exists
    IF EXISTS (SELECT 1 FROM dbo.tServiceTag WHERE ServiceRefID = @ServiceRefID AND TagID = @TagID)
        RETURN 0;

    BEGIN TRY
        BEGIN TRAN;

        INSERT INTO dbo.tServiceTag (ServiceRefID, TagID)
        VALUES (@ServiceRefID, @TagID);

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'ServiceTag',
            @EntityID   = @ServiceRefID,
            @UserID     = @ActingUserID,
            @Action     = 'Add';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE usp_ServiceTag_Remove
    @ServiceRefID         INT,
    @TagID                INT,
    @ActingUserID         INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRAN;

        DELETE FROM dbo.tServiceTag WHERE ServiceRefID = @ServiceRefID AND TagID = @TagID;

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'ServiceTag',
            @EntityID   = @ServiceRefID,
            @UserID     = @ActingUserID,
            @Action     = 'Remove';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE usp_ServiceTag_List
    @ServiceRefID INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT * FROM dbo.tServiceTag WHERE ServiceRefID = @ServiceRefID;
END;
GO


CREATE OR ALTER PROCEDURE usp_CreateTable_tActivityLog
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID('dbo.tActivityLog', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.tActivityLog (
            LogID      BIGINT IDENTITY(1,1) NOT NULL,
            UserID     INT                  NOT NULL,
            EntityType VARCHAR(50)          NOT NULL,
            EntityID   INT                  NOT NULL,
            ActionDate DATETIME2(0)         NOT NULL CONSTRAINT DF_tActivityLog_ActionDate DEFAULT (SYSUTCDATETIME()),
            Action     VARCHAR(30)          NOT NULL,
            FieldName  VARCHAR(100)         NULL,
            OldValue   NVARCHAR(MAX)        NULL,
            NewValue   NVARCHAR(MAX)        NULL,
            CONSTRAINT PK_tActivityLog PRIMARY KEY CLUSTERED (LogID),
            CONSTRAINT FK_tActivityLog_User FOREIGN KEY (UserID) REFERENCES dbo.tUser (UserID),
            CONSTRAINT CK_tActivityLog_Action CHECK (Action IN ('Insert','Update','SoftDelete','Restore','Add','Remove'))
        );

        CREATE INDEX IX_tActivityLog_Entity ON dbo.tActivityLog (EntityType, EntityID, ActionDate DESC);
        CREATE INDEX IX_tActivityLog_User   ON dbo.tActivityLog (UserID, ActionDate DESC);
    END;
END;
GO
CREATE OR ALTER PROCEDURE usp_ActivityLog_Insert
    @EntityType VARCHAR(50),
    @EntityID   INT,
    @UserID     INT,
    @Action     VARCHAR(30),
    @FieldName  VARCHAR(100)  = NULL,
    @OldValue   NVARCHAR(MAX) = NULL,
    @NewValue   NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.tActivityLog (EntityType, EntityID, UserID, Action, FieldName, OldValue, NewValue)
    VALUES (@EntityType, @EntityID, @UserID, @Action, @FieldName, @OldValue, @NewValue);
END;
GO
CREATE OR ALTER PROCEDURE usp_ActivityLog_GetByEntity
    @EntityType VARCHAR(50),
    @EntityID   INT,
    @FromDate   DATETIME2(0) = NULL,
    @ToDate     DATETIME2(0) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT *
      FROM dbo.tActivityLog
     WHERE EntityType = @EntityType
       AND EntityID   = @EntityID
       AND (@FromDate IS NULL OR ActionDate >= @FromDate)
       AND (@ToDate   IS NULL OR ActionDate <= @ToDate)
     ORDER BY ActionDate DESC;
END;
GO
CREATE OR ALTER PROCEDURE usp_ActivityLog_GetByUser
    @UserID   INT,
    @FromDate DATETIME2(0) = NULL,
    @ToDate   DATETIME2(0) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT *
      FROM dbo.tActivityLog
     WHERE UserID = @UserID
       AND (@FromDate IS NULL OR ActionDate >= @FromDate)
       AND (@ToDate   IS NULL OR ActionDate <= @ToDate)
     ORDER BY ActionDate DESC;
END;
GO


CREATE OR ALTER PROCEDURE usp_CreateTable_tUserAddressRef
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID('dbo.tUserAddressRef', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.tUserAddressRef (
            UserID       INT          NOT NULL,
            AddressID    INT          NOT NULL,
            AddressType  VARCHAR(20)  NOT NULL,
            IsPrimary    BIT          NOT NULL CONSTRAINT DF_tUserAddressRef_IsPrimary DEFAULT (0),
            CreatedDate  DATETIME2(0) NOT NULL CONSTRAINT DF_tUserAddressRef_CreatedDate DEFAULT (SYSUTCDATETIME()),
            IsActive     BIT          NOT NULL CONSTRAINT DF_tUserAddressRef_IsActive DEFAULT (1),
            CONSTRAINT PK_tUserAddressRef PRIMARY KEY CLUSTERED (UserID, AddressID, AddressType),
            CONSTRAINT FK_tUserAddressRef_User    FOREIGN KEY (UserID)    REFERENCES dbo.tUser(UserID)       ON DELETE CASCADE,
            CONSTRAINT FK_tUserAddressRef_Address FOREIGN KEY (AddressID) REFERENCES dbo.tAddress(AddressID),
            CONSTRAINT CK_tUserAddressRef_AddressType CHECK (AddressType IN ('Home','Work','Billing','Other'))
        );

        -- Enforce at most one primary per (UserID, AddressType) via filtered unique index
        CREATE UNIQUE INDEX UX_tUserAddressRef_Primary
            ON dbo.tUserAddressRef (UserID, AddressType)
            WHERE IsPrimary = 1;
    END;
END;
GO
CREATE OR ALTER PROCEDURE usp_UserAddressRef_Add
    @UserID       INT,
    @AddressID    INT,
    @AddressType  VARCHAR(20),
    @IsPrimary    BIT = 0,
    @ActingUserID INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRAN;

        -- If marking as primary, clear any existing primary for this user+type
        IF @IsPrimary = 1
        BEGIN
            UPDATE dbo.tUserAddressRef
               SET IsPrimary = 0
             WHERE UserID = @UserID
               AND AddressType = @AddressType
               AND IsPrimary = 1;
        END;

        -- Idempotent insert
        IF NOT EXISTS (
            SELECT 1 FROM dbo.tUserAddressRef
             WHERE UserID = @UserID AND AddressID = @AddressID AND AddressType = @AddressType
        )
        BEGIN
            INSERT INTO dbo.tUserAddressRef (UserID, AddressID, AddressType, IsPrimary)
            VALUES (@UserID, @AddressID, @AddressType, @IsPrimary);

            EXEC dbo.usp_ActivityLog_Insert
                @EntityType = 'UserAddressRef',
                @EntityID   = @UserID,
                @UserID     = @ActingUserID,
                @Action     = 'Insert';
        END;

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE usp_UserAddressRef_Remove
    @UserID       INT,
    @AddressID    INT,
    @AddressType  VARCHAR(20),
    @ActingUserID INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRAN;

        DELETE FROM dbo.tUserAddressRef
         WHERE UserID = @UserID
           AND AddressID = @AddressID
           AND AddressType = @AddressType;

        IF @@ROWCOUNT > 0
        BEGIN
            EXEC dbo.usp_ActivityLog_Insert
                @EntityType = 'UserAddressRef',
                @EntityID   = @UserID,
                @UserID     = @ActingUserID,
                @Action     = 'Delete';
        END;

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE usp_UserAddressRef_List
    @UserID INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT r.UserID,
           r.AddressID,
           r.AddressType,
           r.IsPrimary,
           r.IsActive,
           a.Address1, a.Address2, a.City, a.State, a.PostalCode, a.Country
      FROM dbo.tUserAddressRef r
      JOIN dbo.tAddress a ON a.AddressID = r.AddressID
     WHERE r.UserID = @UserID
       AND r.IsActive = 1
       AND a.IsActive = 1
     ORDER BY r.AddressType, r.IsPrimary DESC;
END;
GO

--Views
CREATE OR ALTER PROCEDURE usp_Create_vHomeTile_ActiveClientCount
AS
BEGIN
    SET NOCOUNT ON;

    EXEC('
CREATE OR ALTER VIEW dbo.v_HomeTile_ActiveClientCount
AS
SELECT ActiveClientCount = COUNT_BIG(DISTINCT c.ClientID)
  FROM dbo.tClient c
 WHERE c.IsActive = 1;
');
END;
GO
CREATE OR ALTER PROCEDURE usp_Create_vHomeTile_ActiveProjectCount
AS
BEGIN
    SET NOCOUNT ON;

    EXEC('
CREATE OR ALTER VIEW dbo.v_HomeTile_ActiveProjectCount
AS
SELECT ActiveProjectCount = COUNT_BIG(*)
  FROM dbo.tProject p
 WHERE p.IsActive = 1;
');
END;
GO
CREATE OR ALTER PROCEDURE usp_Create_vHomeTile_ActiveProjectRevenue
AS
BEGIN
    SET NOCOUNT ON;

    -- Sum of latest revenue per active project (tProjectRevenue is a history table).
    -- OUTER APPLY + COALESCE(...,0) so projects with no revenue rows contribute 0 and the tile returns 0 (not NULL) when no revenue exists.
    EXEC('
CREATE OR ALTER VIEW dbo.v_HomeTile_ActiveProjectRevenue
AS
SELECT ActiveProjectRevenue = COALESCE(SUM(COALESCE(latest.Revenue, 0)), 0)
  FROM dbo.tProject p
 OUTER APPLY (
     SELECT TOP 1 r.Revenue
       FROM dbo.tProjectRevenue r
       JOIN dbo.tProjectRef     pr ON pr.ProjectRefID = r.ProjectRefID
      WHERE pr.ProjectID = p.ProjectID
      ORDER BY r.EffectiveDate DESC, r.RevenueID DESC
 ) latest
 WHERE p.IsActive = 1;
');
END;
GO
CREATE OR ALTER PROCEDURE usp_Create_vProjectList
AS
BEGIN
    SET NOCOUNT ON;

    /*
      vProjectList — one row per active project, wide denormalized shape for the home /
      dashboard grid (same contract as the legacy dbo.vProject_Current).

      Client lifecycle status on the Main grid is StatusDescription from
      dbo.tClientRef.StatusID → dbo.tStatus (not tClient.ClientStatus / IsActive).

      Dynamic columns:
        - One column per active dbo.tUserRole.UserRoleName: members of the project''s
          inferred user group (dbo.tUserGroupRef), pivoted like the Phase I proc.
        - One column per catalog service assigned to the project (CategoryName_ServiceName),
          sourced through dbo.tProjectService → dbo.tServiceRef → dbo.tService /
          dbo.tServiceCategory, pivoted on Frequency (or ''Yes'' when blank).

      dbo.vProject_Current is recreated as SELECT * FROM dbo.vProjectList for callers
      that still invoke usp_Create_vProject_Current.
    */

    DECLARE @columns NVARCHAR(MAX);
    DECLARE @pivotColumns NVARCHAR(MAX);
    DECLARE @colServices NVARCHAR(MAX);
    DECLARE @roleSelect NVARCHAR(MAX) = N'';
    DECLARE @roleJoin NVARCHAR(MAX) = N'';
    DECLARE @serviceSelect NVARCHAR(MAX) = N'';
    DECLARE @serviceJoin NVARCHAR(MAX) = N'';
    DECLARE @sql NVARCHAR(MAX);

    SELECT @columns = STRING_AGG(
        'ISNULL(roleUsers.' + QUOTENAME(UserRoleName) + ', ''n/a'') AS ' + QUOTENAME(UserRoleName),
        ',')
    FROM (
        SELECT DISTINCT UserRoleName
        FROM dbo.tUserRole
        WHERE UserRoleName IS NOT NULL
          AND IsActive = 1
    ) r;

    SELECT @pivotColumns = STRING_AGG(QUOTENAME(UserRoleName), ',')
    FROM (
        SELECT DISTINCT UserRoleName
        FROM dbo.tUserRole
        WHERE UserRoleName IS NOT NULL
          AND IsActive = 1
    ) r;

    IF @columns IS NOT NULL AND @pivotColumns IS NOT NULL
    BEGIN
        SET @roleSelect = N',
    ' + @columns;

        SET @roleJoin = N'
LEFT JOIN
(
    SELECT UserGroupID, ' + @pivotColumns + N'
    FROM
    (
        SELECT
            ugr.UserGroupID,
            ur.UserRoleName,
            STRING_AGG(CONCAT(u.FirstName, '' '', u.LastName), ''; '') AS UserNames
        FROM dbo.tUserGroupRef ugr
        INNER JOIN dbo.tUser u ON u.UserID = ugr.UserID AND u.IsActive = 1
        INNER JOIN dbo.tUserRole ur ON ur.UserRoleID = ugr.UserRoleID AND ur.IsActive = 1
        GROUP BY ugr.UserGroupID, ur.UserRoleName
    ) src
    PIVOT
    (
        MAX(UserNames)
        FOR UserRoleName IN (' + @pivotColumns + N')
    ) pvt
) roleUsers ON projUg.UserGroupID = roleUsers.UserGroupID';
    END;

    SELECT @colServices = STRING_AGG(CatService, ',')
    FROM (
        SELECT DISTINCT
            QUOTENAME(
                LEFT(
                    REPLACE(REPLACE(REPLACE(CONCAT(sc.CategoryName, '_', s.ServiceName), '/', '_'), ' ', '_'), '-', '_'),
                    128
                )
            ) AS CatService
        FROM dbo.tProjectService ps
        INNER JOIN dbo.tProjectRef pr ON pr.ProjectRefID = ps.ProjectRefID AND pr.IsActive = 1
        INNER JOIN dbo.tServiceRef sr ON sr.ServiceRefID = ps.ServiceRefID AND sr.IsActive = 1
        INNER JOIN dbo.tService s ON s.ServiceID = sr.ServiceID AND s.IsActive = 1
        INNER JOIN dbo.tServiceCategory sc ON sc.ServiceCategoryID = sr.ServiceCategoryID AND sc.IsActive = 1
        WHERE ps.IsActive = 1
          AND sc.ServiceCategoryDescription IS NOT NULL
          AND sc.CategoryName IS NOT NULL
          AND s.ServiceName IS NOT NULL
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
            FROM dbo.tProjectService ps
            INNER JOIN dbo.tProjectRef pr ON pr.ProjectRefID = ps.ProjectRefID AND pr.IsActive = 1
            INNER JOIN dbo.tServiceRef sr ON sr.ServiceRefID = ps.ServiceRefID AND sr.IsActive = 1
            INNER JOIN dbo.tService s ON s.ServiceID = sr.ServiceID AND s.IsActive = 1
            INNER JOIN dbo.tServiceCategory sc ON sc.ServiceCategoryID = sr.ServiceCategoryID AND sc.IsActive = 1
            WHERE ps.IsActive = 1
              AND sc.ServiceCategoryDescription IS NOT NULL
              AND sc.CategoryName IS NOT NULL
              AND s.ServiceName IS NOT NULL
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
            pr.ProjectID,
            LEFT(REPLACE(REPLACE(REPLACE(CONCAT(sc.CategoryName, ''_'', s.ServiceName), ''/'', ''_''), '' '', ''_''), ''-'', ''_''), 128) AS CatService,
            COALESCE(NULLIF(ps.Frequency, ''''), ''Yes'') AS ServiceValue
        FROM dbo.tProjectService ps
        INNER JOIN dbo.tProjectRef pr ON pr.ProjectRefID = ps.ProjectRefID AND pr.IsActive = 1
        INNER JOIN dbo.tServiceRef sr ON sr.ServiceRefID = ps.ServiceRefID AND sr.IsActive = 1
        INNER JOIN dbo.tService s ON s.ServiceID = sr.ServiceID AND s.IsActive = 1
        INNER JOIN dbo.tServiceCategory sc ON sc.ServiceCategoryID = sr.ServiceCategoryID AND sc.IsActive = 1
        WHERE ps.IsActive = 1
          AND sc.ServiceCategoryDescription IS NOT NULL
          AND sc.CategoryName IS NOT NULL
          AND s.ServiceName IS NOT NULL
    ) src
    PIVOT
    (
        MAX(ServiceValue)
        FOR CatService IN (' + @colServices + N')
    ) pvt
) projectServices ON p.ProjectID = projectServices.ProjectID';
    END;

    SET @sql = N'
CREATE OR ALTER VIEW dbo.vProjectList
AS
SELECT
    bu.BusinessUnitName,
    BusinessClassName = COALESCE(NULLIF(LTRIM(RTRIM(bc.BusinessClassDescription)), N''''), bc.BusinessClassName),
    c.ClientID,
    c.ClientName,
    StatusDescription = CAST(stClient.StatusDescription AS NVARCHAR(50)),
    p.ProjectID,
    p.ProjectNumber,
    p.ProjectName,
    p.IsOnline,
    PTDescription = pt.ProjectTypeDescription,
    p.RenewalDate,
    p.DueDate,
    ProjectStatus = CASE WHEN p.IsActive = 1 THEN CAST(N''Active'' AS NVARCHAR(50)) ELSE CAST(N''Inactive'' AS NVARCHAR(50)) END,
    lr.RevenueID AS LatestRevenueID,
    lr.EffectiveDate AS LatestRevenueEffectiveDate,
    lr.Revenue AS LatestRevenue,
    RevenueTier = CASE
        WHEN ISNULL(lr.Revenue, 0) <= 0 THEN N''Unknown''
        WHEN lr.Revenue <= 250000 THEN N''Less Than $250K''
        WHEN lr.Revenue <= 500000 THEN N''Less Than $500K''
        WHEN lr.Revenue <= 1000000 THEN N''Less Than $1.0M''
        WHEN lr.Revenue <= 2500000 THEN N''Less Than $2.5M''
        WHEN lr.Revenue <= 5000000 THEN N''Less Than $5.0M''
        WHEN lr.Revenue <= 10000000 THEN N''Less Than $10.0M''
        ELSE N''Needs Reviewed''
    END,
    UGID = projUg.UserGroupID,
    UGName = ug.UserGroupName,
    UserGroupID = projUg.UserGroupID,
    UserGroupName = ug.UserGroupName,
    pref.ProjectRefID,
    p.CreatedDate,
    p.ModifiedDate,
    p.IsActive'
    + @roleSelect
    + @serviceSelect + N'
FROM dbo.tProject p
OUTER APPLY
(
    SELECT TOP (1)
        pr.ProjectRefID,
        pr.ClientRefID,
        pr.ProjectTypeID
    FROM dbo.tProjectRef pr
    WHERE pr.ProjectID = p.ProjectID
      AND pr.IsActive = 1
    ORDER BY pr.ProjectRefID
) pref
INNER JOIN dbo.tClientRef cr ON cr.ClientRefID = pref.ClientRefID AND cr.IsActive = 1
INNER JOIN dbo.tClient c ON c.ClientID = cr.ClientID
LEFT JOIN dbo.tStatus stClient ON stClient.StatusID = cr.StatusID
INNER JOIN dbo.tBusinessUnit bu ON bu.BusinessUnitID = cr.BusinessUnitID
INNER JOIN dbo.tBusinessClass bc ON bc.BusinessClassID = cr.BusinessClassID
INNER JOIN dbo.tProjectType pt ON pt.ProjectTypeID = pref.ProjectTypeID
OUTER APPLY
(
    SELECT TOP (1) ugr.UserGroupID
    FROM dbo.tProjectUser pu
    INNER JOIN dbo.tUserGroupRef ugr ON ugr.UserID = pu.UserID
    WHERE pu.ProjectRefID = pref.ProjectRefID
      AND pu.IsActive = 1
    GROUP BY ugr.UserGroupID
    ORDER BY COUNT(*) DESC, ugr.UserGroupID
) projUg
LEFT JOIN dbo.tUserGroup ug ON ug.UserGroupID = projUg.UserGroupID'
    + @roleJoin
    + @serviceJoin + N'
OUTER APPLY
(
    SELECT TOP (1)
        rev.RevenueID,
        rev.EffectiveDate,
        rev.Revenue
    FROM dbo.tProjectRevenue rev
    WHERE rev.ProjectRefID = pref.ProjectRefID
      AND rev.IsActive = 1
    ORDER BY rev.EffectiveDate DESC, rev.RevenueID DESC
) lr
WHERE p.IsActive = 1;';

    EXEC sys.sp_executesql @sql;

    EXEC(N'
CREATE OR ALTER VIEW dbo.vProject_Current
AS
SELECT *
  FROM dbo.vProjectList;
');
END;
GO
CREATE OR ALTER PROCEDURE usp_Create_vProject_Current
AS
BEGIN
    SET NOCOUNT ON;
    EXEC dbo.usp_Create_vProjectList;
END;
GO

--with Refs
CREATE OR ALTER PROCEDURE usp_BusinessUnit_ListWithRefs
    @ActiveOnly BIT = 1
AS
BEGIN
    SET NOCOUNT ON;

    SELECT bu.BusinessUnitID, bu.BusinessUnitName, bu.BusinessUnitDescription, bu.IsActive
      FROM dbo.tBusinessUnit bu
     WHERE (@ActiveOnly = 0 OR bu.IsActive = 1)
     ORDER BY bu.BusinessUnitName;
END;
GO
CREATE OR ALTER PROCEDURE usp_BusinessClass_ListWithRefs
    @ActiveOnly BIT = 1
AS
BEGIN
    SET NOCOUNT ON;

    SELECT bc.BusinessClassID, bc.BusinessClassName, bc.BusinessClassDescription,
          BusinessUnitName = bu.BusinessUnitName, bc.IsActive
      FROM dbo.tBusinessClass bc
      LEFT JOIN dbo.tBusinessUnit bu ON bu.BusinessUnitID = bc.BusinessUnitID
     WHERE (@ActiveOnly = 0 OR bc.IsActive = 1)
     ORDER BY bu.BusinessUnitName, bc.BusinessClassName;
END;
GO
CREATE OR ALTER PROCEDURE usp_ClientType_ListWithRefs
    @ActiveOnly BIT = 1
AS
BEGIN
    SET NOCOUNT ON;

    SELECT ct.ClientTypeID, ct.ClientTypeName, ct.ClientTypeDescription, ct.IsActive
      FROM dbo.tClientType ct
     WHERE (@ActiveOnly = 0 OR ct.IsActive = 1)
     ORDER BY ct.ClientTypeName;
END;
GO
CREATE OR ALTER PROCEDURE usp_ClientTypeRef_ListWithRefs
    @ActiveOnly BIT = 1
AS
BEGIN
    SET NOCOUNT ON;

    SELECT ctr.ClientTypeRefID, ctr.ClientTypeRefName, ctr.IsActive
      FROM dbo.tClientTypeRef ctr
     WHERE (@ActiveOnly = 0 OR ctr.IsActive = 1)
     ORDER BY ctr.ClientTypeRefName;
END;
GO
CREATE OR ALTER PROCEDURE usp_Tag_ListWithRefs
    @ActiveOnly BIT = 1
AS
BEGIN
    SET NOCOUNT ON;

    SELECT t.TagID, t.TagName, t.TagScope
      FROM dbo.tTag t
     WHERE (@ActiveOnly = 0 OR 1 = 1)
     ORDER BY t.TagScope, t.TagName;
END;
GO
CREATE OR ALTER PROCEDURE usp_ProjectType_ListWithRefs
    @ActiveOnly BIT = 1
AS
BEGIN
    SET NOCOUNT ON;

    SELECT pt.ProjectTypeID, pt.ProjectTypeCode, pt.ProjectTypeDescription, pt.IsActive
      FROM dbo.tProjectType pt
     WHERE (@ActiveOnly = 0 OR pt.IsActive = 1)
     ORDER BY pt.ProjectTypeCode;
END;
GO
CREATE OR ALTER PROCEDURE usp_UserRole_ListWithRefs
    @ActiveOnly BIT = 1
AS
BEGIN
    SET NOCOUNT ON;

    SELECT ur.UserRoleID, ur.UserRoleName, ur.RoleDescription, ur.IsActive,
          Tags = (SELECT STRING_AGG(t.TagName, ', ') WITHIN GROUP (ORDER BY t.TagName)
                    FROM dbo.tUserRoleTag urt JOIN dbo.tTag t ON t.TagID = urt.TagID
                   WHERE urt.UserRoleID = ur.UserRoleID)
      FROM dbo.tUserRole ur
     WHERE (@ActiveOnly = 0 OR ur.IsActive = 1)
     ORDER BY ur.UserRoleName;
END;
GO
CREATE OR ALTER PROCEDURE usp_UserGroup_ListWithRefs
    @ActiveOnly BIT = 1
AS
BEGIN
    SET NOCOUNT ON;

    SELECT ug.UserGroupID, ug.UserGroupNumber, ug.UserGroupName, ug.IsActive,
          UserRoleName = ur.UserRoleName
      FROM dbo.tUserGroup ug
      LEFT JOIN dbo.tUserRole ur ON ur.UserRoleID = ug.UserRoleID
     WHERE (@ActiveOnly = 0 OR ug.IsActive = 1)
     ORDER BY ug.UserGroupName;
END;
GO
CREATE OR ALTER PROCEDURE usp_ServiceGroup_ListWithRefs
    @ActiveOnly BIT = 1
AS
BEGIN
    SET NOCOUNT ON;

    SELECT sg.ServiceGroupID, sg.ServiceGroupNumber, sg.ServiceGroupName, sg.IsActive
      FROM dbo.tServiceGroup sg
     WHERE (@ActiveOnly = 0 OR sg.IsActive = 1)
     ORDER BY sg.ServiceGroupName;
END;
GO
CREATE OR ALTER PROCEDURE usp_ServiceCategory_ListWithRefs
    @ActiveOnly BIT = 1
AS
BEGIN
    SET NOCOUNT ON;

    SELECT sc.ServiceCategoryID, sc.CategoryName, sc.ServiceCategoryDescription, sc.IsActive
      FROM dbo.tServiceCategory sc
     WHERE (@ActiveOnly = 0 OR sc.IsActive = 1)
     ORDER BY sc.CategoryName;
END;
GO
CREATE OR ALTER PROCEDURE usp_Service_ListWithRefs
    @ActiveOnly BIT = 1
AS
BEGIN
    SET NOCOUNT ON;

    SELECT s.ServiceID, s.ServiceName, s.ServiceDescription, s.IsActive,
          CategoryName = sc.CategoryName,
          ServiceGroupName = sg.ServiceGroupName,
          Tags = (SELECT STRING_AGG(t.TagName, ', ') WITHIN GROUP (ORDER BY t.TagName)
                    FROM dbo.tServiceTag st JOIN dbo.tServiceRef sr ON sr.ServiceRefID = st.ServiceRefID
                    JOIN dbo.tTag t ON t.TagID = st.TagID WHERE sr.ServiceID = s.ServiceID)
      FROM dbo.tService s
      OUTER APPLY (SELECT TOP 1 ServiceCategoryID, ServiceGroupID FROM dbo.tServiceRef WHERE ServiceID = s.ServiceID) sr
      LEFT JOIN dbo.tServiceCategory sc ON sc.ServiceCategoryID = sr.ServiceCategoryID
      LEFT JOIN dbo.tServiceGroup    sg ON sg.ServiceGroupID    = sr.ServiceGroupID
     WHERE (@ActiveOnly = 0 OR s.IsActive = 1)
     ORDER BY s.ServiceName;
END;
GO
CREATE OR ALTER PROCEDURE usp_Client_ListWithRefs
    @ActiveOnly BIT = 1
AS
BEGIN
    SET NOCOUNT ON;

    SELECT c.ClientID, c.ClientName, c.ClientShortName, c.ClientDescription, c.IsActive,
          BusinessUnitName = bu.BusinessUnitName,
          BusinessClassName = COALESCE(NULLIF(LTRIM(RTRIM(bc.BusinessClassDescription)), N''), bc.BusinessClassName),
          ClientTypeName = ct.ClientTypeName,
          Tags = (SELECT STRING_AGG(t.TagName, ', ') WITHIN GROUP (ORDER BY t.TagName)
                    FROM dbo.tClientTag ctag JOIN dbo.tTag t ON t.TagID = ctag.TagID
                   WHERE ctag.ClientID = c.ClientID)
      FROM dbo.tClient c
      OUTER APPLY (SELECT TOP 1 BusinessClassID, BusinessUnitID, ClientTypeID FROM dbo.tClientRef WHERE ClientID = c.ClientID) cr
      LEFT JOIN dbo.tBusinessClass bc ON bc.BusinessClassID = cr.BusinessClassID
      LEFT JOIN dbo.tBusinessUnit  bu ON bu.BusinessUnitID  = cr.BusinessUnitID
      LEFT JOIN dbo.tClientType    ct ON ct.ClientTypeID    = cr.ClientTypeID
     WHERE (@ActiveOnly = 0 OR c.IsActive = 1)
     ORDER BY c.ClientName;
END;
GO
CREATE OR ALTER PROCEDURE usp_Project_ListWithRefs
    @ActiveOnly BIT = 1
AS
BEGIN
    SET NOCOUNT ON;

    SELECT p.ProjectID, p.ProjectNumber, p.ProjectName, p.RenewalDate, p.DueDate, p.IsActive,
          ClientName = c.ClientName,
          ProjectTypeCode = pt.ProjectTypeCode,
          LatestRevenue = COALESCE(latest.Revenue, 0),
          Tags = (SELECT STRING_AGG(t.TagName, ', ') WITHIN GROUP (ORDER BY t.TagName)
                    FROM dbo.tProjectTag ptag JOIN dbo.tProjectRef prf ON prf.ProjectRefID = ptag.ProjectRefID
                    JOIN dbo.tTag t ON t.TagID = ptag.TagID WHERE prf.ProjectID = p.ProjectID)
      FROM dbo.tProject p
      OUTER APPLY (SELECT TOP 1 pr.ProjectTypeID, cr.ClientID
                     FROM dbo.tProjectRef pr LEFT JOIN dbo.tClientRef cr ON cr.ClientRefID = pr.ClientRefID
                    WHERE pr.ProjectID = p.ProjectID) pref
      LEFT JOIN dbo.tClient      c  ON c.ClientID      = pref.ClientID
      LEFT JOIN dbo.tProjectType pt ON pt.ProjectTypeID = pref.ProjectTypeID
      OUTER APPLY (SELECT TOP 1 r.Revenue FROM dbo.tProjectRevenue r
                    JOIN dbo.tProjectRef prf ON prf.ProjectRefID = r.ProjectRefID
                   WHERE prf.ProjectID = p.ProjectID ORDER BY r.EffectiveDate DESC, r.RevenueID DESC) latest
     WHERE (@ActiveOnly = 0 OR p.IsActive = 1)
     ORDER BY p.ProjectNumber;
END;
GO
CREATE OR ALTER PROCEDURE usp_User_ListWithRefs
    @ActiveOnly BIT = 1
AS
BEGIN
    SET NOCOUNT ON;

    SELECT u.UserID, u.FirstName, u.LastName, u.Title, u.Email, u.Phone, u.Company, u.Department, u.UserType, u.IsActive,
          Roles = (SELECT STRING_AGG(ur.UserRoleName, ', ') WITHIN GROUP (ORDER BY ur.UserRoleName)
                     FROM dbo.tUserGroupRef ugr JOIN dbo.tUserRole ur ON ur.UserRoleID = ugr.UserRoleID
                    WHERE ugr.UserID = u.UserID),
          PrimaryAddress = pa.Address1
      FROM dbo.tUser u
      OUTER APPLY (SELECT TOP 1 a.Address1
                     FROM dbo.tUserAddressRef ar JOIN dbo.tAddress a ON a.AddressID = ar.AddressID
                    WHERE ar.UserID = u.UserID AND ar.IsPrimary = 1) pa
     WHERE (@ActiveOnly = 0 OR u.IsActive = 1)
     ORDER BY u.LastName, u.FirstName;
END;
GO
CREATE OR ALTER PROCEDURE usp_Address_ListWithRefs
    @ActiveOnly BIT = 1
AS
BEGIN
    SET NOCOUNT ON;

    SELECT a.AddressID, a.Address1, a.Address2, a.City, a.State, a.PostalCode, a.Country, a.IsActive
      FROM dbo.tAddress a
     WHERE (@ActiveOnly = 0 OR a.IsActive = 1)
     ORDER BY a.Country, a.State, a.City;
END;
GO
CREATE OR ALTER PROCEDURE usp_ClientRef_ListWithRefs
    @ActiveOnly BIT = 1
AS
BEGIN
    SET NOCOUNT ON;

    SELECT cr.ClientRefID, ClientName = c.ClientName,
          BusinessUnitName = bu.BusinessUnitName,
          BusinessClassName = COALESCE(NULLIF(LTRIM(RTRIM(bc.BusinessClassDescription)), N''), bc.BusinessClassName),
          ClientTypeName = ct.ClientTypeName, cr.IsActive
      FROM dbo.tClientRef cr
      LEFT JOIN dbo.tClient        c  ON c.ClientID  = cr.ClientID
      LEFT JOIN dbo.tBusinessUnit  bu ON bu.BusinessUnitID = cr.BusinessUnitID
      LEFT JOIN dbo.tBusinessClass bc ON bc.BusinessClassID = cr.BusinessClassID
      LEFT JOIN dbo.tClientType    ct ON ct.ClientTypeID = cr.ClientTypeID
     WHERE (@ActiveOnly = 0 OR cr.IsActive = 1)
     ORDER BY c.ClientName;
END;
GO
CREATE OR ALTER PROCEDURE usp_ProjectRef_ListWithRefs
    @ActiveOnly BIT = 1
AS
BEGIN
    SET NOCOUNT ON;

    SELECT pr.ProjectRefID, ProjectName = p.ProjectName, ProjectNumber = p.ProjectNumber,
          ClientName = c.ClientName, ProjectTypeCode = pt.ProjectTypeCode, pr.IsActive
      FROM dbo.tProjectRef pr
      LEFT JOIN dbo.tProject     p  ON p.ProjectID    = pr.ProjectID
      LEFT JOIN dbo.tClientRef   cr ON cr.ClientRefID = pr.ClientRefID
      LEFT JOIN dbo.tClient      c  ON c.ClientID     = cr.ClientID
      LEFT JOIN dbo.tProjectType pt ON pt.ProjectTypeID = pr.ProjectTypeID
     WHERE (@ActiveOnly = 0 OR pr.IsActive = 1)
     ORDER BY p.ProjectNumber;
END;
GO
CREATE OR ALTER PROCEDURE usp_ServiceRef_ListWithRefs
    @ActiveOnly BIT = 1
AS
BEGIN
    SET NOCOUNT ON;

    SELECT sr.ServiceRefID, ServiceName = s.ServiceName,
          CategoryName = sc.CategoryName, ServiceGroupName = sg.ServiceGroupName, sr.IsActive
      FROM dbo.tServiceRef sr
      LEFT JOIN dbo.tService         s  ON s.ServiceID         = sr.ServiceID
      LEFT JOIN dbo.tServiceCategory sc ON sc.ServiceCategoryID = sr.ServiceCategoryID
      LEFT JOIN dbo.tServiceGroup    sg ON sg.ServiceGroupID    = sr.ServiceGroupID
     WHERE (@ActiveOnly = 0 OR sr.IsActive = 1)
     ORDER BY s.ServiceName;
END;
GO
CREATE OR ALTER PROCEDURE usp_ProjectNote_ListWithRefs
    @ActiveOnly BIT = 1
AS
BEGIN
    SET NOCOUNT ON;

    SELECT pn.NoteID, pn.NoteDate, pn.NoteType, pn.NoteText, pn.IsPinned, pn.CreatedDate,
          ProjectName = p.ProjectName, ProjectNumber = p.ProjectNumber
      FROM dbo.tProjectNote pn
      LEFT JOIN dbo.tProjectRef pr ON pr.ProjectRefID = pn.ProjectRefID
      LEFT JOIN dbo.tProject    p  ON p.ProjectID    = pr.ProjectID
     WHERE (@ActiveOnly = 0 OR 1 = 1)
     ORDER BY pn.NoteDate DESC;
END;
GO
CREATE OR ALTER PROCEDURE usp_ProjectRevenue_ListWithRefs
    @ActiveOnly BIT = 1
AS
BEGIN
    SET NOCOUNT ON;

    SELECT pr_rev.RevenueID, pr_rev.EffectiveDate, pr_rev.Revenue, pr_rev.ChangeReason, pr_rev.CreatedDate,
          ProjectName = p.ProjectName, ProjectNumber = p.ProjectNumber,
          RecordedByName = CONCAT(u.FirstName, ' ', u.LastName)
      FROM dbo.tProjectRevenue pr_rev
      LEFT JOIN dbo.tProjectRef pr ON pr.ProjectRefID = pr_rev.ProjectRefID
      LEFT JOIN dbo.tProject    p  ON p.ProjectID    = pr.ProjectID
      LEFT JOIN dbo.tUser       u  ON u.UserID       = pr_rev.RecordedBy
     WHERE (@ActiveOnly = 0 OR 1 = 1)
     ORDER BY pr_rev.EffectiveDate DESC;
END;
GO
CREATE OR ALTER PROCEDURE usp_ProjectService_ListWithRefs
    @ActiveOnly BIT = 1
AS
BEGIN
    SET NOCOUNT ON;

    SELECT ps.ProjectServiceID, ps.Frequency, ps.StartDate, ps.EndDate, ps.IsActive,
          ProjectName = p.ProjectName, ServiceName = s.ServiceName
      FROM dbo.tProjectService ps
      LEFT JOIN dbo.tProjectRef pr ON pr.ProjectRefID = ps.ProjectRefID
      LEFT JOIN dbo.tProject    p  ON p.ProjectID    = pr.ProjectID
      LEFT JOIN dbo.tServiceRef sr ON sr.ServiceRefID = ps.ServiceRefID
      LEFT JOIN dbo.tService    s  ON s.ServiceID    = sr.ServiceID
     WHERE (@ActiveOnly = 0 OR ps.IsActive = 1)
     ORDER BY p.ProjectNumber, s.ServiceName;
END;
GO
CREATE OR ALTER PROCEDURE usp_ProjectUser_ListWithRefs
    @ActiveOnly BIT = 1
AS
BEGIN
    SET NOCOUNT ON;

    SELECT pu.ProjectUserID, pu.StartDate, pu.EndDate, pu.IsActive,
          ProjectName = p.ProjectName, ProjectNumber = p.ProjectNumber,
          UserName = CONCAT(u.FirstName, ' ', u.LastName),
          UserRoleName = ur.UserRoleName
      FROM dbo.tProjectUser pu
      LEFT JOIN dbo.tProjectRef pr ON pr.ProjectRefID = pu.ProjectRefID
      LEFT JOIN dbo.tProject    p  ON p.ProjectID    = pr.ProjectID
      LEFT JOIN dbo.tUser       u  ON u.UserID       = pu.UserID
      LEFT JOIN dbo.tUserRole   ur ON ur.UserRoleID  = pu.UserRoleID
     WHERE (@ActiveOnly = 0 OR pu.IsActive = 1)
     ORDER BY p.ProjectNumber, u.LastName;
END;
GO


CREATE OR ALTER PROCEDURE usp_UserGroupRef_Set
    @UserGroupID    INT,
    @ChildIDs        dbo.IntList READONLY,    -- TVP: single INT column 'Value'
    @ActingUserID    INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRAN;

        -- Delete rows no longer in the set
        DELETE j
          FROM dbo.tUserGroupRef j
         WHERE j.UserGroupID = @UserGroupID
           AND NOT EXISTS (SELECT 1 FROM @ChildIDs c WHERE c.Value = j.UserID);

        -- Insert rows that are new
        INSERT INTO dbo.tUserGroupRef (UserGroupID, UserID)
        SELECT @UserGroupID, c.Value
          FROM @ChildIDs c
         WHERE NOT EXISTS (
             SELECT 1 FROM dbo.tUserGroupRef j
              WHERE j.UserGroupID = @UserGroupID AND j.UserID = c.Value
         );

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'UserGroupRef',
            @EntityID   = @UserGroupID,
            @UserID     = @ActingUserID,
            @Action     = 'Set';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE usp_BusinessClassTag_Set
    @BusinessClassID    INT,
    @ChildIDs        dbo.IntList READONLY,    -- TVP: single INT column 'Value'
    @ActingUserID    INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRAN;

        -- Delete rows no longer in the set
        DELETE j
          FROM dbo.tBusinessClassTag j
         WHERE j.BusinessClassID = @BusinessClassID
           AND NOT EXISTS (SELECT 1 FROM @ChildIDs c WHERE c.Value = j.TagID);

        -- Insert rows that are new
        INSERT INTO dbo.tBusinessClassTag (BusinessClassID, TagID)
        SELECT @BusinessClassID, c.Value
          FROM @ChildIDs c
         WHERE NOT EXISTS (
             SELECT 1 FROM dbo.tBusinessClassTag j
              WHERE j.BusinessClassID = @BusinessClassID AND j.TagID = c.Value
         );

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'BusinessClassTag',
            @EntityID   = @BusinessClassID,
            @UserID     = @ActingUserID,
            @Action     = 'Set';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE usp_UserRoleTag_Set
    @UserRoleID    INT,
    @ChildIDs        dbo.IntList READONLY,    -- TVP: single INT column 'Value'
    @ActingUserID    INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRAN;

        -- Delete rows no longer in the set
        DELETE j
          FROM dbo.tUserRoleTag j
         WHERE j.UserRoleID = @UserRoleID
           AND NOT EXISTS (SELECT 1 FROM @ChildIDs c WHERE c.Value = j.TagID);

        -- Insert rows that are new
        INSERT INTO dbo.tUserRoleTag (UserRoleID, TagID)
        SELECT @UserRoleID, c.Value
          FROM @ChildIDs c
         WHERE NOT EXISTS (
             SELECT 1 FROM dbo.tUserRoleTag j
              WHERE j.UserRoleID = @UserRoleID AND j.TagID = c.Value
         );

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'UserRoleTag',
            @EntityID   = @UserRoleID,
            @UserID     = @ActingUserID,
            @Action     = 'Set';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE usp_ClientTag_Set
    @ClientID    INT,
    @ChildIDs        dbo.IntList READONLY,    -- TVP: single INT column 'Value'
    @ActingUserID    INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRAN;

        -- Delete rows no longer in the set
        DELETE j
          FROM dbo.tClientTag j
         WHERE j.ClientID = @ClientID
           AND NOT EXISTS (SELECT 1 FROM @ChildIDs c WHERE c.Value = j.TagID);

        -- Insert rows that are new
        INSERT INTO dbo.tClientTag (ClientID, TagID)
        SELECT @ClientID, c.Value
          FROM @ChildIDs c
         WHERE NOT EXISTS (
             SELECT 1 FROM dbo.tClientTag j
              WHERE j.ClientID = @ClientID AND j.TagID = c.Value
         );

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'ClientTag',
            @EntityID   = @ClientID,
            @UserID     = @ActingUserID,
            @Action     = 'Set';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE usp_ProjectTag_Set
    @ProjectRefID    INT,
    @ChildIDs        dbo.IntList READONLY,    -- TVP: single INT column 'Value'
    @ActingUserID    INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRAN;

        -- Delete rows no longer in the set
        DELETE j
          FROM dbo.tProjectTag j
         WHERE j.ProjectRefID = @ProjectRefID
           AND NOT EXISTS (SELECT 1 FROM @ChildIDs c WHERE c.Value = j.TagID);

        -- Insert rows that are new
        INSERT INTO dbo.tProjectTag (ProjectRefID, TagID)
        SELECT @ProjectRefID, c.Value
          FROM @ChildIDs c
         WHERE NOT EXISTS (
             SELECT 1 FROM dbo.tProjectTag j
              WHERE j.ProjectRefID = @ProjectRefID AND j.TagID = c.Value
         );

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'ProjectTag',
            @EntityID   = @ProjectRefID,
            @UserID     = @ActingUserID,
            @Action     = 'Set';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE usp_ServiceTag_Set
    @ServiceRefID    INT,
    @ChildIDs        dbo.IntList READONLY,    -- TVP: single INT column 'Value'
    @ActingUserID    INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRAN;

        -- Delete rows no longer in the set
        DELETE j
          FROM dbo.tServiceTag j
         WHERE j.ServiceRefID = @ServiceRefID
           AND NOT EXISTS (SELECT 1 FROM @ChildIDs c WHERE c.Value = j.TagID);

        -- Insert rows that are new
        INSERT INTO dbo.tServiceTag (ServiceRefID, TagID)
        SELECT @ServiceRefID, c.Value
          FROM @ChildIDs c
         WHERE NOT EXISTS (
             SELECT 1 FROM dbo.tServiceTag j
              WHERE j.ServiceRefID = @ServiceRefID AND j.TagID = c.Value
         );

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'ServiceTag',
            @EntityID   = @ServiceRefID,
            @UserID     = @ActingUserID,
            @Action     = 'Set';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE usp_UserAddressRef_Set
    @UserID         INT,
    @Addresses      dbo.UserAddressList READONLY,    -- TVP: (AddressID INT, AddressType VARCHAR(20), IsPrimary BIT)
    @ActingUserID   INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRAN;

        -- Delete rows no longer in the set
        DELETE j
          FROM dbo.tUserAddressRef j
         WHERE j.UserID = @UserID
           AND NOT EXISTS (
               SELECT 1 FROM @Addresses a
                WHERE a.AddressID = j.AddressID
                  AND a.AddressType = j.AddressType
           );

        -- Insert rows that are new
        INSERT INTO dbo.tUserAddressRef (UserID, AddressID, AddressType, IsPrimary)
        SELECT @UserID, a.AddressID, a.AddressType, a.IsPrimary
          FROM @Addresses a
         WHERE NOT EXISTS (
             SELECT 1 FROM dbo.tUserAddressRef j
              WHERE j.UserID = @UserID
                AND j.AddressID = a.AddressID
                AND j.AddressType = a.AddressType
         );

        -- Update IsPrimary for rows that exist with a different IsPrimary flag
        UPDATE j
           SET IsPrimary = a.IsPrimary
          FROM dbo.tUserAddressRef j
          JOIN @Addresses a
            ON a.AddressID  = j.AddressID
           AND a.AddressType = j.AddressType
         WHERE j.UserID = @UserID
           AND j.IsPrimary <> a.IsPrimary;

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'UserAddressRef',
            @EntityID   = @UserID,
            @UserID     = @ActingUserID,
            @Action     = 'Set';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE usp_CreateType_IntList
AS
BEGIN
    SET NOCOUNT ON;

    IF TYPE_ID('dbo.IntList') IS NULL
    BEGIN
        CREATE TYPE dbo.IntList AS TABLE (
            Value INT NOT NULL,
            PRIMARY KEY (Value)
        );
    END;
END;
GO
CREATE OR ALTER PROCEDURE usp_CreateType_UserAddressList
AS
BEGIN
    SET NOCOUNT ON;

    IF TYPE_ID('dbo.UserAddressList') IS NULL
    BEGIN
        CREATE TYPE dbo.UserAddressList AS TABLE (
            AddressID   INT          NOT NULL,
            AddressType VARCHAR(20)  NOT NULL,
            IsPrimary   BIT          NOT NULL DEFAULT (0),
            PRIMARY KEY (AddressID, AddressType)
        );
    END;
END;
GO

CREATE OR ALTER PROCEDURE usp_Bootstrap
AS
BEGIN
    SET NOCOUNT ON;

    /*
      One-shot installer. Runs every _CreateTable / _CreateType / _Create_v* SP
      in dependency order so a fresh DB can be stood up with EXEC dbo.usp_Bootstrap.
      All called SPs are idempotent (IF OBJECT_ID / IF TYPE_ID guards), so this
      is safe to re-run after schema changes.
    */

    ------------------------------------------------------------------
    -- 1. Table-valued parameter types (must exist before _Set SPs)
    ------------------------------------------------------------------
    EXEC dbo.usp_CreateType_IntList;
    EXEC dbo.usp_CreateType_UserAddressList;

    ------------------------------------------------------------------
    -- 2. Lookup / reference tables (no FKs to other app tables)
    ------------------------------------------------------------------
    EXEC dbo.usp_CreateTable_tBusinessUnit;
    EXEC dbo.usp_CreateTable_tStatus;              -- lookup: Active / Inactive / Prospect
    EXEC dbo.usp_CreateTable_tBusinessClass;       -- FK → tBusinessUnit
    EXEC dbo.usp_CreateTable_tClientType;
    EXEC dbo.usp_CreateTable_tClientTypeRef;
    EXEC dbo.usp_CreateTable_tProjectType;
    EXEC dbo.usp_CreateTable_tTag;
    EXEC dbo.usp_CreateTable_tUserRole;
    EXEC dbo.usp_CreateTable_tServiceGroup;
    EXEC dbo.usp_CreateTable_tServiceCategory;

    ------------------------------------------------------------------
    -- 3. Core entities
    ------------------------------------------------------------------
    EXEC dbo.usp_CreateTable_tClient;              -- user FKs added in 3b (after tUser)
    EXEC dbo.usp_CreateTable_tUserGroup;
    EXEC dbo.usp_CreateTable_tUser;
    EXEC dbo.usp_AlterTable_tClient_AddUserFKs;    -- FK → tUser (AE, AnalyticsLead)
    EXEC dbo.usp_CreateTable_tAddress;
    EXEC dbo.usp_CreateTable_tService;
    EXEC dbo.usp_CreateTable_tProject;
    EXEC dbo.usp_AlterTable_tProject_AddIsOnline;  -- adds IsOnline for in-progress indicator (idempotent)

    ------------------------------------------------------------------
    -- 4. Ref / detail tables (depend on entities above)
    ------------------------------------------------------------------
    EXEC dbo.usp_CreateTable_tClientRef;           -- FK → tClient, tBusinessUnit, tBusinessClass, tClientType, tClientTypeRef, tStatus
    EXEC dbo.usp_AlterTable_tClientRef_AddStatus;  -- adds StatusID to existing installs (idempotent)
    EXEC dbo.usp_AlterTable_tClientRef_NullableClassification; -- allow partial BU/BC/CT (idempotent)
    EXEC dbo.usp_CreateTable_tProjectRef;          -- FK → tProject, tClientRef, tProjectType
    EXEC dbo.usp_AlterTable_tProjectRef_AddStatusAndUserGroup; -- adds StatusID + UserGroupID (idempotent)
    EXEC dbo.usp_CreateTable_tServiceRef;          -- FK → tServiceCategory, tServiceGroup, tService
    EXEC dbo.usp_CreateTable_tProjectNote;         -- FK → tProjectRef
    EXEC dbo.usp_CreateTable_tProjectRevenue;      -- FK → tProjectRef
    EXEC dbo.usp_CreateTable_tProjectService;      -- FK → tProjectRef, tServiceRef
    EXEC dbo.usp_CreateTable_tProjectUser;         -- FK → tProjectRef, tUser, tUserRole

    ------------------------------------------------------------------
    -- 5. Junctions (composite-PK tag/membership tables)
    ------------------------------------------------------------------
    EXEC dbo.usp_CreateTable_tUserGroupRef;        -- FK → tUserGroup, tUser, tUserRole
    EXEC dbo.usp_CreateTable_tUserAddressRef;      -- FK → tUser, tAddress
    EXEC dbo.usp_CreateTable_tBusinessClassTag;    -- FK → tBusinessClass, tTag
    EXEC dbo.usp_CreateTable_tUserRoleTag;         -- FK → tUserRole, tTag
    EXEC dbo.usp_CreateTable_tClientTag;           -- FK → tClient, tTag
    EXEC dbo.usp_CreateTable_tProjectTag;          -- FK → tProjectRef, tTag
    EXEC dbo.usp_CreateTable_tServiceTag;          -- FK → tServiceRef, tTag

    ------------------------------------------------------------------
    -- 6. Audit log (depends on tUser)
    ------------------------------------------------------------------
    EXEC dbo.usp_CreateTable_tActivityLog;

    ------------------------------------------------------------------
    -- 7. Home-screen views (depend on everything above)
    ------------------------------------------------------------------
    EXEC dbo.usp_Create_vHomeTile_ActiveClientCount;
    EXEC dbo.usp_Create_vHomeTile_ActiveProjectCount;
    EXEC dbo.usp_Create_vHomeTile_ActiveProjectRevenue;
    EXEC dbo.usp_Create_vProjectList;

    PRINT 'usp_Bootstrap complete.';
END;
GO

CREATE OR ALTER PROCEDURE usp_BuildInstallScript
AS
BEGIN
    SET NOCOUNT ON;

    /*
      Emits the full SP install script as a result set.
      Run from SSMS with 'Results to Text' (Ctrl+T) and save to .sql,
      or pipe via sqlcmd:

          sqlcmd -S <server> -d <db> -E -h -1 -y 0 -W \
                 -Q EXEC dbo.usp_BuildInstallScript -o install.sql

      Requires a populated dbo.tSPCatalog with columns:
          SPNumber  INT NOT NULL  -- ordering
          SPName    SYSNAME       -- (informational)
          SPBody    NVARCHAR(MAX) -- the full CREATE OR ALTER PROCEDURE ... body

      Each row is emitted followed by a GO terminator on its own line so
      sqlcmd / SSMS executes them as separate batches (CREATE OR ALTER
      PROCEDURE must be the first statement in a batch).
    */

    IF OBJECT_ID('dbo.tSPCatalog', 'U') IS NULL
        THROW 50001, 'dbo.tSPCatalog is missing. Load SP Bodies into it first.', 1;

    SELECT ScriptLine
      FROM (
            SELECT SPNumber, Ord = 1, ScriptLine = CAST(SPBody AS NVARCHAR(MAX))
              FROM dbo.tSPCatalog
            UNION ALL
            SELECT SPNumber, Ord = 2, ScriptLine = N'GO'
              FROM dbo.tSPCatalog
           ) x
     ORDER BY SPNumber, Ord;
END;
GO