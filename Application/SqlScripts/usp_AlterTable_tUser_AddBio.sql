-- Adds dbo.tUser.Bio (profile biography, up to 250 words). Safe to re-run.
CREATE OR ALTER PROCEDURE dbo.usp_AlterTable_tUser_AddBio
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID('dbo.tUser', 'U') IS NULL
        THROW 50001, 'dbo.tUser does not exist. Run usp_CreateTable_tUser first.', 1;

    IF COL_LENGTH('dbo.tUser', 'Bio') IS NULL
    BEGIN
        ALTER TABLE dbo.tUser ADD Bio NVARCHAR(4000) NULL;
    END;
END;
GO

EXEC dbo.usp_AlterTable_tUser_AddBio;
GO
