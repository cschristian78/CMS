/*
  Analytics Calendar for projects — deliverables / meetings / tasks with ownership,
  received date, completed date, and notes. Linked via ProjectRefID (same pattern as
  tProjectNote / tProjectRevenue).

  Idempotent: safe to re-run.
*/
SET NOCOUNT ON;
GO

IF OBJECT_ID(N'dbo.tProjectCalendarItem', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tProjectCalendarItem
    (
        CalendarItemID   INT            IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_tProjectCalendarItem PRIMARY KEY CLUSTERED,
        ProjectRefID     INT            NOT NULL
            CONSTRAINT FK_tProjectCalendarItem_ProjectRefID
                REFERENCES dbo.tProjectRef (ProjectRefID),
        ItemType         VARCHAR(30)    NOT NULL
            CONSTRAINT CK_tProjectCalendarItem_ItemType
                CHECK (ItemType IN ('Deliverable', 'Meeting', 'Task', 'Other')),
        Title            NVARCHAR(200)  NOT NULL,
        OwnerUserID      INT            NULL
            CONSTRAINT FK_tProjectCalendarItem_OwnerUserID
                REFERENCES dbo.tUser (UserID),
        ReceivedDate     DATE           NULL,
        CompletedDate    DATE           NULL,
        PercentComplete  TINYINT        NOT NULL
            CONSTRAINT DF_tProjectCalendarItem_PercentComplete DEFAULT (0)
            CONSTRAINT CK_tProjectCalendarItem_PercentComplete CHECK (PercentComplete BETWEEN 0 AND 100),
        Notes            NVARCHAR(MAX)  NULL,
        SortOrder        INT            NOT NULL
            CONSTRAINT DF_tProjectCalendarItem_SortOrder DEFAULT (0),
        IsActive         BIT            NOT NULL
            CONSTRAINT DF_tProjectCalendarItem_IsActive DEFAULT (1),
        CreatedDate      DATETIME2(0)   NOT NULL
            CONSTRAINT DF_tProjectCalendarItem_CreatedDate DEFAULT (SYSUTCDATETIME()),
        ModifiedDate     DATETIME2(0)   NULL
    );
END
GO

SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'dbo.tProjectCalendarItem', N'U') IS NOT NULL
   AND NOT EXISTS (
        SELECT 1
          FROM sys.indexes
         WHERE name = N'IX_tProjectCalendarItem_ProjectRefID'
           AND object_id = OBJECT_ID(N'dbo.tProjectCalendarItem'))
BEGIN
    CREATE INDEX IX_tProjectCalendarItem_ProjectRefID
        ON dbo.tProjectCalendarItem (ProjectRefID)
        WHERE IsActive = 1;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_ProjectCalendarItem_ListByProject
    @ProjectID INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        ci.CalendarItemID,
        p.ProjectID,
        ci.ProjectRefID,
        ci.ItemType,
        ci.Title,
        ci.OwnerUserID,
        OwnerFullName = CASE
            WHEN u.UserID IS NULL THEN NULL
            ELSE LTRIM(RTRIM(CONCAT(u.FirstName, N' ', u.LastName)))
        END,
        ci.ReceivedDate,
        ci.CompletedDate,
        ci.PercentComplete,
        ci.Notes,
        ci.SortOrder,
        ci.IsActive,
        ci.CreatedDate,
        ci.ModifiedDate
      FROM dbo.tProjectCalendarItem AS ci
      INNER JOIN dbo.tProjectRef AS pr ON pr.ProjectRefID = ci.ProjectRefID AND pr.IsActive = 1
      INNER JOIN dbo.tProject AS p ON p.ProjectID = pr.ProjectID
      LEFT JOIN dbo.tUser AS u ON u.UserID = ci.OwnerUserID
     WHERE p.ProjectID = @ProjectID
       AND ci.IsActive = 1
     ORDER BY ci.SortOrder, ci.CalendarItemID;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_ProjectCalendarItem_Create
    @ProjectRefID     INT,
    @ItemType         VARCHAR(30),
    @Title            NVARCHAR(200),
    @OwnerUserID      INT = NULL,
    @ReceivedDate     DATE = NULL,
    @CompletedDate    DATE = NULL,
    @PercentComplete  TINYINT = 0,
    @Notes            NVARCHAR(MAX) = NULL,
    @SortOrder        INT = 0,
    @ActingUserID     INT,
    @NewCalendarItemID INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @ItemType NOT IN ('Deliverable', 'Meeting', 'Task', 'Other')
        THROW 50001, 'ItemType must be Deliverable, Meeting, Task, or Other.', 1;

    IF NULLIF(LTRIM(RTRIM(@Title)), N'') IS NULL
        THROW 50002, 'Title is required.', 1;

    IF @PercentComplete > 100
        THROW 50004, 'PercentComplete must be between 0 and 100.', 1;

    IF NOT EXISTS (SELECT 1 FROM dbo.tProjectRef WHERE ProjectRefID = @ProjectRefID AND IsActive = 1)
        THROW 50003, 'ProjectRef not found or inactive.', 1;

    BEGIN TRY
        BEGIN TRAN;

        INSERT INTO dbo.tProjectCalendarItem
        (
            ProjectRefID, ItemType, Title, OwnerUserID,
            ReceivedDate, CompletedDate, PercentComplete, Notes, SortOrder
        )
        VALUES
        (
            @ProjectRefID,
            @ItemType,
            LTRIM(RTRIM(@Title)),
            @OwnerUserID,
            @ReceivedDate,
            @CompletedDate,
            @PercentComplete,
            @Notes,
            @SortOrder
        );

        SET @NewCalendarItemID = SCOPE_IDENTITY();

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'ProjectCalendarItem',
            @EntityID   = @NewCalendarItemID,
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

CREATE OR ALTER PROCEDURE dbo.usp_ProjectCalendarItem_Update
    @CalendarItemID   INT,
    @ItemType         VARCHAR(30),
    @Title            NVARCHAR(200),
    @OwnerUserID      INT = NULL,
    @ReceivedDate     DATE = NULL,
    @CompletedDate    DATE = NULL,
    @PercentComplete  TINYINT = 0,
    @Notes            NVARCHAR(MAX) = NULL,
    @SortOrder        INT = 0,
    @ActingUserID     INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.tProjectCalendarItem WHERE CalendarItemID = @CalendarItemID AND IsActive = 1)
        THROW 50001, 'Calendar item not found or inactive.', 1;

    IF @ItemType NOT IN ('Deliverable', 'Meeting', 'Task', 'Other')
        THROW 50002, 'ItemType must be Deliverable, Meeting, Task, or Other.', 1;

    IF NULLIF(LTRIM(RTRIM(@Title)), N'') IS NULL
        THROW 50003, 'Title is required.', 1;

    IF @PercentComplete > 100
        THROW 50004, 'PercentComplete must be between 0 and 100.', 1;

    BEGIN TRY
        BEGIN TRAN;

        UPDATE dbo.tProjectCalendarItem
           SET ItemType = @ItemType,
               Title = LTRIM(RTRIM(@Title)),
               OwnerUserID = @OwnerUserID,
               ReceivedDate = @ReceivedDate,
               CompletedDate = @CompletedDate,
               PercentComplete = @PercentComplete,
               Notes = @Notes,
               SortOrder = @SortOrder,
               ModifiedDate = SYSUTCDATETIME()
         WHERE CalendarItemID = @CalendarItemID;

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'ProjectCalendarItem',
            @EntityID   = @CalendarItemID,
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

CREATE OR ALTER PROCEDURE dbo.usp_ProjectCalendarItem_SoftDelete
    @CalendarItemID INT,
    @ActingUserID   INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.tProjectCalendarItem WHERE CalendarItemID = @CalendarItemID AND IsActive = 1)
        THROW 50001, 'Calendar item not found or already inactive.', 1;

    BEGIN TRY
        BEGIN TRAN;

        UPDATE dbo.tProjectCalendarItem
           SET IsActive = 0,
               ModifiedDate = SYSUTCDATETIME()
         WHERE CalendarItemID = @CalendarItemID;

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'ProjectCalendarItem',
            @EntityID   = @CalendarItemID,
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

PRINT 'tProjectCalendarItem + USPs ready.';
GO
