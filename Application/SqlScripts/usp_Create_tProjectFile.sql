/*
  Project client-data file uploads — metadata in SQL, bytes in Azure Blob (cms container).

  Idempotent: safe to re-run.
  IsActive     = soft-delete (row present vs removed)
  IsCurrent    = "Active" for processing (at most one per ProjectRefID among live rows)
  Description  = free-text notes
  VersionLabel = version control label (e.g. v1, 2026-08-12)
  DataAsOf     = data-as-of date for the uploaded dataset
*/
SET NOCOUNT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'dbo.tProjectFile', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tProjectFile
    (
        ProjectFileID    INT             IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_tProjectFile PRIMARY KEY CLUSTERED,
        ProjectRefID     INT             NOT NULL
            CONSTRAINT FK_tProjectFile_ProjectRefID
                REFERENCES dbo.tProjectRef (ProjectRefID),
        OriginalFileName NVARCHAR(260)   NOT NULL,
        ContentType      NVARCHAR(150)   NULL,
        ByteLength       BIGINT          NOT NULL
            CONSTRAINT DF_tProjectFile_ByteLength DEFAULT (0),
        BlobPath         NVARCHAR(500)   NOT NULL,
        BlobUrl          NVARCHAR(1000)  NOT NULL,
        Category         VARCHAR(40)     NOT NULL
            CONSTRAINT DF_tProjectFile_Category DEFAULT ('SourceData')
            CONSTRAINT CK_tProjectFile_Category
                CHECK (Category IN ('SourceData', 'ExposureData', 'LossData', 'ClaimsData', 'CorrespondenceData', 'SubmissionData')),
        Description      NVARCHAR(1000)  NULL,
        VersionLabel     NVARCHAR(40)    NULL,
        DataAsOf         DATE            NULL,
        IsCurrent        BIT             NOT NULL
            CONSTRAINT DF_tProjectFile_IsCurrent DEFAULT (0),
        UploadedByUserID INT             NOT NULL
            CONSTRAINT FK_tProjectFile_UploadedByUserID
                REFERENCES dbo.tUser (UserID),
        IsActive         BIT             NOT NULL
            CONSTRAINT DF_tProjectFile_IsActive DEFAULT (1),
        CreatedDate      DATETIME2(0)    NOT NULL
            CONSTRAINT DF_tProjectFile_CreatedDate DEFAULT (SYSUTCDATETIME()),
        ModifiedDate     DATETIME2(0)    NULL
    );
END
GO

SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'dbo.tProjectFile', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.tProjectFile', N'Description') IS NULL
BEGIN
    ALTER TABLE dbo.tProjectFile ADD Description NVARCHAR(1000) NULL;
END
GO

IF OBJECT_ID(N'dbo.tProjectFile', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.tProjectFile', N'VersionLabel') IS NULL
BEGIN
    ALTER TABLE dbo.tProjectFile ADD VersionLabel NVARCHAR(40) NULL;
END
GO

IF OBJECT_ID(N'dbo.tProjectFile', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.tProjectFile', N'DataAsOf') IS NULL
BEGIN
    ALTER TABLE dbo.tProjectFile ADD DataAsOf DATE NULL;
END
GO

IF OBJECT_ID(N'dbo.tProjectFile', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.tProjectFile', N'IsCurrent') IS NULL
BEGIN
    ALTER TABLE dbo.tProjectFile ADD IsCurrent BIT NOT NULL
        CONSTRAINT DF_tProjectFile_IsCurrent DEFAULT (0);
END
GO

IF OBJECT_ID(N'dbo.tProjectFile', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.tProjectFile', N'Category') IS NULL
BEGIN
    ALTER TABLE dbo.tProjectFile
        ADD Category VARCHAR(40) NOT NULL
            CONSTRAINT DF_tProjectFile_Category_Add DEFAULT ('SourceData');
END
GO

IF OBJECT_ID(N'dbo.tProjectFile', N'U') IS NOT NULL
BEGIN
    /* Migrate legacy categories → SourceData, then refresh CHECK constraint. */
    UPDATE dbo.tProjectFile
       SET Category = 'SourceData'
     WHERE Category IN ('ClientData', 'Contract', 'Invoice', 'Other')
        OR Category IS NULL
        OR LTRIM(RTRIM(Category)) = '';

    IF EXISTS (
        SELECT 1
          FROM sys.check_constraints
         WHERE name = N'CK_tProjectFile_Category'
           AND parent_object_id = OBJECT_ID(N'dbo.tProjectFile'))
    BEGIN
        ALTER TABLE dbo.tProjectFile DROP CONSTRAINT CK_tProjectFile_Category;
    END

    ALTER TABLE dbo.tProjectFile WITH NOCHECK
        ADD CONSTRAINT CK_tProjectFile_Category
            CHECK (Category IN ('SourceData', 'ExposureData', 'LossData', 'ClaimsData', 'CorrespondenceData', 'SubmissionData'));

    DECLARE @dfName sysname =
    (
        SELECT dc.name
          FROM sys.default_constraints AS dc
         WHERE dc.parent_object_id = OBJECT_ID(N'dbo.tProjectFile')
           AND COL_NAME(dc.parent_object_id, dc.parent_column_id) = N'Category'
    );
    IF @dfName IS NOT NULL
    BEGIN
        EXEC(N'ALTER TABLE dbo.tProjectFile DROP CONSTRAINT [' + @dfName + N']');
    END
    ALTER TABLE dbo.tProjectFile
        ADD CONSTRAINT DF_tProjectFile_Category DEFAULT ('SourceData') FOR Category;
END
GO

IF OBJECT_ID(N'dbo.tProjectFile', N'U') IS NOT NULL
   AND EXISTS (
        SELECT 1
          FROM sys.indexes
         WHERE name = N'UX_tProjectFile_OneCurrentPerRef'
           AND object_id = OBJECT_ID(N'dbo.tProjectFile'))
BEGIN
    DROP INDEX UX_tProjectFile_OneCurrentPerRef ON dbo.tProjectFile;
END
GO

IF OBJECT_ID(N'dbo.tProjectFile', N'U') IS NOT NULL
BEGIN
    ;WITH ranked AS
    (
        SELECT
            ProjectFileID,
            ROW_NUMBER() OVER (
                PARTITION BY ProjectRefID, Category
                ORDER BY CASE WHEN IsCurrent = 1 THEN 0 ELSE 1 END, ProjectFileID DESC
            ) AS rn
          FROM dbo.tProjectFile
         WHERE IsActive = 1
           AND IsCurrent = 1
    )
    UPDATE f
       SET IsCurrent = 0,
           ModifiedDate = SYSUTCDATETIME()
      FROM dbo.tProjectFile AS f
      INNER JOIN ranked AS r ON r.ProjectFileID = f.ProjectFileID
     WHERE r.rn > 1;
END
GO

IF OBJECT_ID(N'dbo.tProjectFile', N'U') IS NOT NULL
   AND NOT EXISTS (
        SELECT 1
          FROM sys.indexes
         WHERE name = N'UX_tProjectFile_OneCurrentPerCategory'
           AND object_id = OBJECT_ID(N'dbo.tProjectFile'))
BEGIN
    /* At most one Active file per project ref + classification. */
    CREATE UNIQUE INDEX UX_tProjectFile_OneCurrentPerCategory
        ON dbo.tProjectFile (ProjectRefID, Category)
        WHERE IsActive = 1 AND IsCurrent = 1;
END
GO

IF OBJECT_ID(N'dbo.tProjectFile', N'U') IS NOT NULL
   AND NOT EXISTS (
        SELECT 1
          FROM sys.indexes
         WHERE name = N'IX_tProjectFile_ProjectRefID'
           AND object_id = OBJECT_ID(N'dbo.tProjectFile'))
BEGIN
    CREATE INDEX IX_tProjectFile_ProjectRefID
        ON dbo.tProjectFile (ProjectRefID)
        WHERE IsActive = 1;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_ProjectFile_ListByProject
    @ProjectID INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        f.ProjectFileID,
        p.ProjectID,
        f.ProjectRefID,
        f.OriginalFileName,
        f.ContentType,
        f.ByteLength,
        f.BlobPath,
        f.BlobUrl,
        f.Category,
        f.Description,
        f.VersionLabel,
        f.DataAsOf,
        f.IsCurrent,
        f.UploadedByUserID,
        UploadedByName = CASE
            WHEN u.UserID IS NULL THEN NULL
            ELSE LTRIM(RTRIM(CONCAT(u.FirstName, N' ', u.LastName)))
        END,
        f.IsActive,
        f.CreatedDate,
        f.ModifiedDate
      FROM dbo.tProjectFile AS f
      INNER JOIN dbo.tProjectRef AS pr ON pr.ProjectRefID = f.ProjectRefID AND pr.IsActive = 1
      INNER JOIN dbo.tProject AS p ON p.ProjectID = pr.ProjectID
      LEFT JOIN dbo.tUser AS u ON u.UserID = f.UploadedByUserID
     WHERE p.ProjectID = @ProjectID
       AND f.IsActive = 1
     ORDER BY f.IsCurrent DESC, f.CreatedDate DESC, f.ProjectFileID DESC;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_ProjectFile_Create
    @ProjectRefID      INT,
    @OriginalFileName  NVARCHAR(260),
    @ContentType       NVARCHAR(150) = NULL,
    @ByteLength        BIGINT,
    @BlobPath          NVARCHAR(500),
    @BlobUrl           NVARCHAR(1000),
    @Category          VARCHAR(40) = 'SourceData',
    @Description       NVARCHAR(1000) = NULL,
    @VersionLabel      NVARCHAR(40) = NULL,
    @DataAsOf          DATE = NULL,
    @IsCurrent         BIT = 0,
    @UploadedByUserID  INT,
    @ActingUserID      INT,
    @NewProjectFileID  INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NULLIF(LTRIM(RTRIM(@OriginalFileName)), N'') IS NULL
        THROW 50001, 'OriginalFileName is required.', 1;

    IF NULLIF(LTRIM(RTRIM(@BlobPath)), N'') IS NULL
        THROW 50002, 'BlobPath is required.', 1;

    IF @Category NOT IN ('SourceData', 'ExposureData', 'LossData', 'ClaimsData', 'CorrespondenceData', 'SubmissionData')
        SET @Category = 'SourceData';

    IF NOT EXISTS (SELECT 1 FROM dbo.tProjectRef WHERE ProjectRefID = @ProjectRefID AND IsActive = 1)
        THROW 50003, 'ProjectRef not found or inactive.', 1;

    SET @Description = NULLIF(LTRIM(RTRIM(@Description)), N'');
    SET @VersionLabel = NULLIF(LTRIM(RTRIM(@VersionLabel)), N'');
    SET @IsCurrent = ISNULL(@IsCurrent, 0);

    BEGIN TRY
        BEGIN TRAN;

        IF @IsCurrent = 1
        BEGIN
            UPDATE dbo.tProjectFile
               SET IsCurrent = 0,
                   ModifiedDate = SYSUTCDATETIME()
             WHERE ProjectRefID = @ProjectRefID
               AND Category = @Category
               AND IsActive = 1
               AND IsCurrent = 1;
        END

        INSERT INTO dbo.tProjectFile
        (
            ProjectRefID, OriginalFileName, ContentType, ByteLength,
            BlobPath, BlobUrl, Category, Description, VersionLabel, DataAsOf, IsCurrent, UploadedByUserID
        )
        VALUES
        (
            @ProjectRefID,
            LTRIM(RTRIM(@OriginalFileName)),
            @ContentType,
            @ByteLength,
            LTRIM(RTRIM(@BlobPath)),
            LTRIM(RTRIM(@BlobUrl)),
            @Category,
            @Description,
            @VersionLabel,
            @DataAsOf,
            @IsCurrent,
            @UploadedByUserID
        );

        SET @NewProjectFileID = SCOPE_IDENTITY();

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'ProjectFile',
            @EntityID   = @NewProjectFileID,
            @UserID     = @ActingUserID,
            @Action     = 'Insert';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_ProjectFile_UpdateMetadata
    @ProjectFileID INT,
    @Category      VARCHAR(40) = NULL,
    @Description   NVARCHAR(1000) = NULL,
    @VersionLabel  NVARCHAR(40) = NULL,
    @DataAsOf      DATE = NULL,
    @ActingUserID  INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.tProjectFile WHERE ProjectFileID = @ProjectFileID AND IsActive = 1)
        THROW 50001, 'Project file not found or inactive.', 1;

    IF @Category IS NOT NULL
       AND @Category NOT IN ('SourceData', 'ExposureData', 'LossData', 'ClaimsData', 'CorrespondenceData', 'SubmissionData')
        SET @Category = 'SourceData';

    SET @Description = NULLIF(LTRIM(RTRIM(@Description)), N'');
    SET @VersionLabel = NULLIF(LTRIM(RTRIM(@VersionLabel)), N'');

    BEGIN TRY
        BEGIN TRAN;

        UPDATE dbo.tProjectFile
           SET Category = COALESCE(@Category, Category),
               Description = @Description,
               VersionLabel = @VersionLabel,
               DataAsOf = @DataAsOf,
               ModifiedDate = SYSUTCDATETIME()
         WHERE ProjectFileID = @ProjectFileID
           AND IsActive = 1;

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'ProjectFile',
            @EntityID   = @ProjectFileID,
            @UserID     = @ActingUserID,
            @Action     = 'Update';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_ProjectFile_SetCurrent
    @ProjectFileID INT,
    @IsCurrent     BIT,
    @ActingUserID  INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @ProjectRefID INT;
    DECLARE @Category VARCHAR(40);

    SELECT @ProjectRefID = ProjectRefID,
           @Category = Category
      FROM dbo.tProjectFile
     WHERE ProjectFileID = @ProjectFileID
       AND IsActive = 1;

    IF @ProjectRefID IS NULL
        THROW 50001, 'Project file not found or inactive.', 1;

    BEGIN TRY
        BEGIN TRAN;

        IF @IsCurrent = 1
        BEGIN
            UPDATE dbo.tProjectFile
               SET IsCurrent = 0,
                   ModifiedDate = SYSUTCDATETIME()
             WHERE ProjectRefID = @ProjectRefID
               AND Category = @Category
               AND IsActive = 1
               AND IsCurrent = 1
               AND ProjectFileID <> @ProjectFileID;

            UPDATE dbo.tProjectFile
               SET IsCurrent = 1,
                   ModifiedDate = SYSUTCDATETIME()
             WHERE ProjectFileID = @ProjectFileID
               AND IsActive = 1;
        END
        ELSE
        BEGIN
            UPDATE dbo.tProjectFile
               SET IsCurrent = 0,
                   ModifiedDate = SYSUTCDATETIME()
             WHERE ProjectFileID = @ProjectFileID
               AND IsActive = 1;
        END

        DECLARE @OldIsCurrent NVARCHAR(10) = CASE WHEN @IsCurrent = 1 THEN N'0' ELSE N'1' END;
        DECLARE @NewIsCurrent NVARCHAR(10) = CASE WHEN @IsCurrent = 1 THEN N'1' ELSE N'0' END;

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'ProjectFile',
            @EntityID   = @ProjectFileID,
            @UserID     = @ActingUserID,
            @Action     = 'Update',
            @FieldName  = 'IsCurrent',
            @OldValue   = @OldIsCurrent,
            @NewValue   = @NewIsCurrent;

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_ProjectFile_SoftDelete
    @ProjectFileID INT,
    @ActingUserID  INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.tProjectFile WHERE ProjectFileID = @ProjectFileID AND IsActive = 1)
        THROW 50001, 'Project file not found or already inactive.', 1;

    BEGIN TRY
        BEGIN TRAN;

        UPDATE dbo.tProjectFile
           SET IsActive = 0,
               IsCurrent = 0,
               ModifiedDate = SYSUTCDATETIME()
         WHERE ProjectFileID = @ProjectFileID;

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'ProjectFile',
            @EntityID   = @ProjectFileID,
            @UserID     = @ActingUserID,
            @Action     = 'SoftDelete';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_ProjectFile_GetById
    @ProjectFileID INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        f.ProjectFileID,
        p.ProjectID,
        f.ProjectRefID,
        f.OriginalFileName,
        f.ContentType,
        f.ByteLength,
        f.BlobPath,
        f.BlobUrl,
        f.Category,
        f.Description,
        f.VersionLabel,
        f.DataAsOf,
        f.IsCurrent,
        f.UploadedByUserID,
        UploadedByName = CASE
            WHEN u.UserID IS NULL THEN NULL
            ELSE LTRIM(RTRIM(CONCAT(u.FirstName, N' ', u.LastName)))
        END,
        f.IsActive,
        f.CreatedDate,
        f.ModifiedDate
      FROM dbo.tProjectFile AS f
      INNER JOIN dbo.tProjectRef AS pr ON pr.ProjectRefID = f.ProjectRefID
      INNER JOIN dbo.tProject AS p ON p.ProjectID = pr.ProjectID
      LEFT JOIN dbo.tUser AS u ON u.UserID = f.UploadedByUserID
     WHERE f.ProjectFileID = @ProjectFileID
       AND f.IsActive = 1;
END
GO

PRINT 'tProjectFile + USPs ready (IsCurrent / Description / VersionLabel / DataAsOf).';
GO
