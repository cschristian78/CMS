-- Legacy bit kept equal to WorkStatus = In Progress. Project status is WorkStatus.
-- The green indicator is In Progress, not a separate Online / Offline tag. Safe to re-run.
CREATE OR ALTER PROCEDURE dbo.usp_AlterTable_tProject_AddIsOnline
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

EXEC dbo.usp_AlterTable_tProject_AddIsOnline;
GO

-- Recreate the home list view so IsOnline is projected (requires updated usp_Create_vProjectList).
IF OBJECT_ID('dbo.usp_Create_vProject_Current', 'P') IS NOT NULL
    EXEC dbo.usp_Create_vProject_Current;
GO
