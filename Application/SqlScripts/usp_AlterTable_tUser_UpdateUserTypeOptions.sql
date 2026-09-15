-- Replaces UserType options: Basic (was Internal), External, Administrator.
-- Migrates existing Internal rows to Basic and promotes Scott Christian to Administrator.
-- Safe to re-run.
CREATE OR ALTER PROCEDURE dbo.usp_AlterTable_tUser_UpdateUserTypeOptions
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID('dbo.tUser', 'U') IS NULL
        THROW 50001, 'dbo.tUser does not exist. Run usp_CreateTable_tUser first.', 1;

    IF EXISTS (
        SELECT 1
          FROM sys.check_constraints
         WHERE name = N'CK_tUser_UserType'
           AND parent_object_id = OBJECT_ID(N'dbo.tUser')
    )
    BEGIN
        ALTER TABLE dbo.tUser DROP CONSTRAINT CK_tUser_UserType;
    END;

    UPDATE dbo.tUser
       SET UserType = N'Basic',
           ModifiedDate = SYSUTCDATETIME()
     WHERE UserType = N'Internal';

    UPDATE dbo.tUser
       SET UserType = N'Administrator',
           ModifiedDate = SYSUTCDATETIME()
     WHERE (
               LOWER(LTRIM(RTRIM(Email))) = N'scott.christian@bmsgroup.com'
            OR (
                   LOWER(LTRIM(RTRIM(FirstName))) = N'scott'
               AND LOWER(LTRIM(RTRIM(LastName))) = N'christian'
               )
           )
       AND IsActive = 1;

    IF NOT EXISTS (
        SELECT 1
          FROM sys.check_constraints
         WHERE name = N'CK_tUser_UserType'
           AND parent_object_id = OBJECT_ID(N'dbo.tUser')
    )
    BEGIN
        ALTER TABLE dbo.tUser
            ADD CONSTRAINT CK_tUser_UserType
            CHECK (UserType IN (N'Basic', N'External', N'Administrator'));
    END;
END;
GO

EXEC dbo.usp_AlterTable_tUser_UpdateUserTypeOptions;
GO
