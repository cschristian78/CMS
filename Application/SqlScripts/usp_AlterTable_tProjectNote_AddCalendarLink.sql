-- Idempotent: link calendar notes into tProjectNote history.
-- Columns, FKs, and index must be separate batches so SQL Server can bind new columns.
CREATE OR ALTER PROCEDURE dbo.usp_AlterTable_tProjectNote_AddCalendarLink
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID(N'dbo.tProjectNote', N'U') IS NULL
        RETURN;

    IF COL_LENGTH(N'dbo.tProjectNote', N'CreatedByUserID') IS NULL
    BEGIN
        ALTER TABLE dbo.tProjectNote ADD CreatedByUserID INT NULL;
    END;

    IF COL_LENGTH(N'dbo.tProjectNote', N'CalendarItemID') IS NULL
    BEGIN
        ALTER TABLE dbo.tProjectNote ADD CalendarItemID INT NULL;
    END;

    IF COL_LENGTH(N'dbo.tProjectNote', N'CalendarItemTitle') IS NULL
    BEGIN
        ALTER TABLE dbo.tProjectNote ADD CalendarItemTitle NVARCHAR(200) NULL;
    END;
END;
GO

EXEC dbo.usp_AlterTable_tProjectNote_AddCalendarLink;
GO

IF OBJECT_ID(N'dbo.tProjectNote', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.tProjectNote', N'CreatedByUserID') IS NOT NULL
   AND OBJECT_ID(N'dbo.FK_tProjectNote_CreatedByUserID', N'F') IS NULL
   AND OBJECT_ID(N'dbo.tUser', N'U') IS NOT NULL
BEGIN
    ALTER TABLE dbo.tProjectNote
        ADD CONSTRAINT FK_tProjectNote_CreatedByUserID
        FOREIGN KEY (CreatedByUserID) REFERENCES dbo.tUser (UserID);
END;
GO

IF OBJECT_ID(N'dbo.tProjectNote', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.tProjectNote', N'CalendarItemID') IS NOT NULL
   AND OBJECT_ID(N'dbo.FK_tProjectNote_CalendarItemID', N'F') IS NULL
   AND OBJECT_ID(N'dbo.tProjectCalendarItem', N'U') IS NOT NULL
BEGIN
    ALTER TABLE dbo.tProjectNote
        ADD CONSTRAINT FK_tProjectNote_CalendarItemID
        FOREIGN KEY (CalendarItemID) REFERENCES dbo.tProjectCalendarItem (CalendarItemID);
END;
GO

IF OBJECT_ID(N'dbo.tProjectNote', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.tProjectNote', N'CalendarItemID') IS NOT NULL
   AND NOT EXISTS (
        SELECT 1
          FROM sys.indexes
         WHERE name = N'IX_tProjectNote_CalendarItemID'
           AND object_id = OBJECT_ID(N'dbo.tProjectNote')
    )
BEGIN
    SET QUOTED_IDENTIFIER ON;
    CREATE NONCLUSTERED INDEX IX_tProjectNote_CalendarItemID
        ON dbo.tProjectNote (CalendarItemID)
        WHERE CalendarItemID IS NOT NULL;
END;
GO
