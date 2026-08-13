/*
  Weekly time tracker for project calendar services / tasks.
  One active row per (project, calendar item, work date, user).
  EntryMode: Exact = typed per cell; Allocated = distributed from a block of hours.

  Idempotent: safe to re-run.
*/
SET NOCOUNT ON;
GO

IF OBJECT_ID(N'dbo.tProjectTimeEntry', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tProjectTimeEntry
    (
        TimeEntryID      INT             IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_tProjectTimeEntry PRIMARY KEY CLUSTERED,
        ProjectRefID     INT             NOT NULL
            CONSTRAINT FK_tProjectTimeEntry_ProjectRefID
                REFERENCES dbo.tProjectRef (ProjectRefID),
        CalendarItemID   INT             NOT NULL
            CONSTRAINT FK_tProjectTimeEntry_CalendarItemID
                REFERENCES dbo.tProjectCalendarItem (CalendarItemID),
        WorkDate         DATE            NOT NULL,
        Hours            DECIMAL(5,2)    NOT NULL
            CONSTRAINT CK_tProjectTimeEntry_Hours CHECK (Hours >= 0 AND Hours <= 24),
        EntryMode        VARCHAR(20)     NOT NULL
            CONSTRAINT DF_tProjectTimeEntry_EntryMode DEFAULT ('Exact')
            CONSTRAINT CK_tProjectTimeEntry_EntryMode CHECK (EntryMode IN ('Exact', 'Allocated')),
        Notes            NVARCHAR(500)   NULL,
        UserID           INT             NOT NULL
            CONSTRAINT FK_tProjectTimeEntry_UserID
                REFERENCES dbo.tUser (UserID),
        IsActive         BIT             NOT NULL
            CONSTRAINT DF_tProjectTimeEntry_IsActive DEFAULT (1),
        CreatedDate      DATETIME2(0)    NOT NULL
            CONSTRAINT DF_tProjectTimeEntry_CreatedDate DEFAULT (SYSUTCDATETIME()),
        ModifiedDate     DATETIME2(0)    NULL
    );
END
GO

SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'dbo.tProjectTimeEntry', N'U') IS NOT NULL
   AND NOT EXISTS (
        SELECT 1
          FROM sys.indexes
         WHERE name = N'IX_tProjectTimeEntry_ProjectWeek'
           AND object_id = OBJECT_ID(N'dbo.tProjectTimeEntry'))
BEGIN
    CREATE INDEX IX_tProjectTimeEntry_ProjectWeek
        ON dbo.tProjectTimeEntry (ProjectRefID, WorkDate, UserID)
        WHERE IsActive = 1;
END
GO

IF OBJECT_ID(N'dbo.tProjectTimeEntry', N'U') IS NOT NULL
   AND NOT EXISTS (
        SELECT 1
          FROM sys.indexes
         WHERE name = N'UX_tProjectTimeEntry_ActiveDayTask'
           AND object_id = OBJECT_ID(N'dbo.tProjectTimeEntry'))
BEGIN
    CREATE UNIQUE INDEX UX_tProjectTimeEntry_ActiveDayTask
        ON dbo.tProjectTimeEntry (ProjectRefID, CalendarItemID, WorkDate, UserID)
        WHERE IsActive = 1;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_ProjectTimeEntry_ListByProjectWeek
    @ProjectID     INT,
    @WeekStartDate DATE,
    @UserID        INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @WeekEnd DATE = DATEADD(DAY, 6, @WeekStartDate);

    SELECT
        te.TimeEntryID,
        p.ProjectID,
        te.ProjectRefID,
        te.CalendarItemID,
        ci.Title AS CalendarItemTitle,
        ci.ItemType AS CalendarItemType,
        te.WorkDate,
        te.Hours,
        te.EntryMode,
        te.Notes,
        te.UserID,
        te.IsActive,
        te.CreatedDate,
        te.ModifiedDate
      FROM dbo.tProjectTimeEntry AS te
      INNER JOIN dbo.tProjectRef AS pr ON pr.ProjectRefID = te.ProjectRefID AND pr.IsActive = 1
      INNER JOIN dbo.tProject AS p ON p.ProjectID = pr.ProjectID
      INNER JOIN dbo.tProjectCalendarItem AS ci ON ci.CalendarItemID = te.CalendarItemID AND ci.IsActive = 1
     WHERE p.ProjectID = @ProjectID
       AND te.IsActive = 1
       AND te.WorkDate >= @WeekStartDate
       AND te.WorkDate <= @WeekEnd
       AND (@UserID IS NULL OR te.UserID = @UserID)
     ORDER BY ci.SortOrder, ci.CalendarItemID, te.WorkDate;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_ProjectTimeEntry_Upsert
    @ProjectRefID    INT,
    @CalendarItemID  INT,
    @WorkDate        DATE,
    @Hours           DECIMAL(5,2),
    @EntryMode       VARCHAR(20) = 'Exact',
    @Notes           NVARCHAR(500) = NULL,
    @ActingUserID    INT,
    @NewTimeEntryID  INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @Hours < 0 OR @Hours > 24
        THROW 50001, 'Hours must be between 0 and 24.', 1;

    IF @EntryMode NOT IN ('Exact', 'Allocated')
        THROW 50002, 'EntryMode must be Exact or Allocated.', 1;

    IF NOT EXISTS (SELECT 1 FROM dbo.tProjectRef WHERE ProjectRefID = @ProjectRefID AND IsActive = 1)
        THROW 50003, 'ProjectRef not found or inactive.', 1;

    IF NOT EXISTS (
        SELECT 1
          FROM dbo.tProjectCalendarItem
         WHERE CalendarItemID = @CalendarItemID
           AND ProjectRefID = @ProjectRefID
           AND IsActive = 1)
        THROW 50004, 'Calendar item not found for this project.', 1;

    BEGIN TRY
        BEGIN TRAN;

        DECLARE @ExistingID INT =
        (
            SELECT TOP (1) TimeEntryID
              FROM dbo.tProjectTimeEntry
             WHERE ProjectRefID = @ProjectRefID
               AND CalendarItemID = @CalendarItemID
               AND WorkDate = @WorkDate
               AND UserID = @ActingUserID
               AND IsActive = 1
        );

        IF @Hours = 0
        BEGIN
            IF @ExistingID IS NOT NULL
            BEGIN
                UPDATE dbo.tProjectTimeEntry
                   SET IsActive = 0,
                       ModifiedDate = SYSUTCDATETIME()
                 WHERE TimeEntryID = @ExistingID;

                EXEC dbo.usp_ActivityLog_Insert
                    @EntityType = 'ProjectTimeEntry',
                    @EntityID   = @ExistingID,
                    @UserID     = @ActingUserID,
                    @Action     = 'SoftDelete';
            END

            SET @NewTimeEntryID = ISNULL(@ExistingID, 0);
        END
        ELSE IF @ExistingID IS NOT NULL
        BEGIN
            UPDATE dbo.tProjectTimeEntry
               SET Hours = @Hours,
                   EntryMode = @EntryMode,
                   Notes = @Notes,
                   ModifiedDate = SYSUTCDATETIME()
             WHERE TimeEntryID = @ExistingID;

            SET @NewTimeEntryID = @ExistingID;

            EXEC dbo.usp_ActivityLog_Insert
                @EntityType = 'ProjectTimeEntry',
                @EntityID   = @NewTimeEntryID,
                @UserID     = @ActingUserID,
                @Action     = 'Update';
        END
        ELSE
        BEGIN
            INSERT INTO dbo.tProjectTimeEntry
            (
                ProjectRefID, CalendarItemID, WorkDate, Hours, EntryMode, Notes, UserID
            )
            VALUES
            (
                @ProjectRefID, @CalendarItemID, @WorkDate, @Hours, @EntryMode, @Notes, @ActingUserID
            );

            SET @NewTimeEntryID = SCOPE_IDENTITY();

            EXEC dbo.usp_ActivityLog_Insert
                @EntityType = 'ProjectTimeEntry',
                @EntityID   = @NewTimeEntryID,
                @UserID     = @ActingUserID,
                @Action     = 'Insert';
        END

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END
GO

PRINT 'tProjectTimeEntry + USPs ready.';
GO
