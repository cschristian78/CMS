-- Idempotent: add PercentComplete (0-100) to Analytics Calendar rows.
-- ADD COLUMN and CHECK must be separate batches (GO) so SQL Server can bind the new column.
CREATE OR ALTER PROCEDURE dbo.usp_AlterTable_tProjectCalendarItem_AddPercentComplete
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID(N'dbo.tProjectCalendarItem', N'U') IS NULL
        RETURN;

    IF COL_LENGTH(N'dbo.tProjectCalendarItem', N'PercentComplete') IS NULL
    BEGIN
        ALTER TABLE dbo.tProjectCalendarItem
            ADD PercentComplete TINYINT NOT NULL
                CONSTRAINT DF_tProjectCalendarItem_PercentComplete DEFAULT (0);
    END;
END;
GO

EXEC dbo.usp_AlterTable_tProjectCalendarItem_AddPercentComplete;
GO

IF OBJECT_ID(N'dbo.tProjectCalendarItem', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.tProjectCalendarItem', N'PercentComplete') IS NOT NULL
   AND OBJECT_ID(N'dbo.CK_tProjectCalendarItem_PercentComplete', N'C') IS NULL
BEGIN
    ALTER TABLE dbo.tProjectCalendarItem
        ADD CONSTRAINT CK_tProjectCalendarItem_PercentComplete
        CHECK (PercentComplete BETWEEN 0 AND 100);
END;
GO
