-- Adds IsFeeBasedRevenue to dbo.tProject for the Project Info fee-based revenue checkbox.
-- Safe to re-run.
CREATE OR ALTER PROCEDURE dbo.usp_AlterTable_tProject_AddIsFeeBasedRevenue
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID('dbo.tProject', 'U') IS NULL
        THROW 50001, 'dbo.tProject does not exist. Run usp_CreateTable_tProject first.', 1;

    IF COL_LENGTH('dbo.tProject', 'IsFeeBasedRevenue') IS NULL
    BEGIN
        ALTER TABLE dbo.tProject
            ADD IsFeeBasedRevenue BIT NOT NULL CONSTRAINT DF_tProject_IsFeeBasedRevenue DEFAULT (0);
    END;
END;
GO

EXEC dbo.usp_AlterTable_tProject_AddIsFeeBasedRevenue;
GO
