-- Adds dbo.tUser.UserFacts (Something about me). Safe to re-run.
CREATE OR ALTER PROCEDURE dbo.usp_AlterTable_tUser_AddUserFacts
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID('dbo.tUser', 'U') IS NULL
        THROW 50001, 'dbo.tUser does not exist. Run usp_CreateTable_tUser first.', 1;

    IF COL_LENGTH('dbo.tUser', 'UserFacts') IS NULL
    BEGIN
        ALTER TABLE dbo.tUser ADD UserFacts NVARCHAR(1000) NULL;
    END;
END;
GO

EXEC dbo.usp_AlterTable_tUser_AddUserFacts;
GO
