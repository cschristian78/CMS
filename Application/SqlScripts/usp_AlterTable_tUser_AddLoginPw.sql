-- Adds LoginPw / LoginCount / LastLoginUtc to dbo.tUser for CAT login tracking.
-- LoginPw defaults to 'none' (first-time password setup). Stored values are PBKDF2 hashes
-- (ASP.NET Core PasswordHasher), not plaintext. LoginCount starts at 0. Safe to re-run.
CREATE OR ALTER PROCEDURE dbo.usp_AlterTable_tUser_AddLoginPw
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID('dbo.tUser', 'U') IS NULL
        THROW 50001, 'dbo.tUser does not exist. Run usp_CreateTable_tUser first.', 1;

    IF COL_LENGTH('dbo.tUser', 'LoginPw') IS NULL
    BEGIN
        ALTER TABLE dbo.tUser
            ADD LoginPw VARCHAR(500) NOT NULL
                CONSTRAINT DF_tUser_LoginPw DEFAULT ('none');
    END
    ELSE IF EXISTS (
        SELECT 1
          FROM sys.columns
         WHERE object_id = OBJECT_ID(N'dbo.tUser')
           AND name = N'LoginPw'
           AND max_length > 0
           AND max_length < 500
    )
    BEGIN
        ALTER TABLE dbo.tUser ALTER COLUMN LoginPw VARCHAR(500) NOT NULL;
    END;

    IF COL_LENGTH('dbo.tUser', 'LoginCount') IS NULL
    BEGIN
        ALTER TABLE dbo.tUser
            ADD LoginCount INT NOT NULL
                CONSTRAINT DF_tUser_LoginCount DEFAULT (0);
    END;

    IF COL_LENGTH('dbo.tUser', 'LastLoginUtc') IS NULL
    BEGIN
        ALTER TABLE dbo.tUser
            ADD LastLoginUtc DATETIME2(0) NULL;
    END;
END;
GO

EXEC dbo.usp_AlterTable_tUser_AddLoginPw;
GO
