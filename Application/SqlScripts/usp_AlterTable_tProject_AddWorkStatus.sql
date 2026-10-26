/*
    Adds dbo.tProject.WorkStatus (Planned, In Progress, Closed).

    One-time backfill (blank WorkStatus only):
      IsOnline = 1                         -> In Progress
      RenewalDate after today             -> Planned
      RenewalDate before today            -> Closed
      RenewalDate today                   -> In Progress
      otherwise                           -> Planned

    IsOnline stays in sync: 1 only when WorkStatus is In Progress.
    The application also applies this on startup.
*/
SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.tProject', N'U') IS NULL
    RETURN;

IF COL_LENGTH(N'dbo.tProject', N'WorkStatus') IS NULL
BEGIN
    ALTER TABLE dbo.tProject ADD WorkStatus VARCHAR(20) NULL;
END;
GO

UPDATE dbo.tProject
   SET WorkStatus = CASE
           WHEN ISNULL(IsOnline, 0) = 1 THEN N'In Progress'
           WHEN RenewalDate > CAST(SYSDATETIME() AS date) THEN N'Planned'
           WHEN RenewalDate < CAST(SYSDATETIME() AS date) THEN N'Closed'
           WHEN RenewalDate = CAST(SYSDATETIME() AS date) THEN N'In Progress'
           ELSE N'Planned'
       END
 WHERE WorkStatus IS NULL
    OR LTRIM(RTRIM(WorkStatus)) = N'';

UPDATE dbo.tProject
   SET IsOnline = CASE WHEN WorkStatus = N'In Progress' THEN 1 ELSE 0 END
 WHERE ISNULL(IsOnline, 0) <> CASE WHEN WorkStatus = N'In Progress' THEN 1 ELSE 0 END;
GO

IF NOT EXISTS (
    SELECT 1
      FROM sys.default_constraints
     WHERE name = N'DF_tProject_WorkStatus'
       AND parent_object_id = OBJECT_ID(N'dbo.tProject')
)
BEGIN
    ALTER TABLE dbo.tProject
        ADD CONSTRAINT DF_tProject_WorkStatus DEFAULT (N'Planned') FOR WorkStatus;
END;

IF EXISTS (
    SELECT 1
      FROM sys.columns
     WHERE object_id = OBJECT_ID(N'dbo.tProject')
       AND name = N'WorkStatus'
       AND is_nullable = 1
)
BEGIN
    ALTER TABLE dbo.tProject ALTER COLUMN WorkStatus VARCHAR(20) NOT NULL;
END;

IF NOT EXISTS (
    SELECT 1
      FROM sys.check_constraints
     WHERE name = N'CK_tProject_WorkStatus'
       AND parent_object_id = OBJECT_ID(N'dbo.tProject')
)
BEGIN
    ALTER TABLE dbo.tProject
        ADD CONSTRAINT CK_tProject_WorkStatus
        CHECK (WorkStatus IN (N'Planned', N'In Progress', N'Closed'));
END;
