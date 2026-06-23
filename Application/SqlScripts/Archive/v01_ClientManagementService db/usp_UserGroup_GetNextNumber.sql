-- dbo.usp_UserGroup_GetNextNumber
-- Returns the next available UserGroupNumber for a brand-new group.
-- Logic: MAX(UserGroupNumber) + 1 over distinct, non-null values in dbo.tUserGroup.
-- Returns 1 when the table is empty (or contains only NULLs).
-- Result column name: NextUserGroupNumber (int, NOT NULL).
-- Safe to re-run (CREATE OR ALTER); requires SQL Server 2016 SP1+.

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

CREATE OR ALTER PROCEDURE dbo.usp_UserGroup_GetNextNumber
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        CAST(ISNULL(MAX(d.UserGroupNumber), 0) + 1 AS int) AS NextUserGroupNumber
    FROM
    (
        SELECT DISTINCT UserGroupNumber
        FROM   dbo.tUserGroup
        WHERE  UserGroupNumber IS NOT NULL
    ) AS d;
END
GO

-- Smoke test (optional — uncomment to verify after deploy):
-- EXEC dbo.usp_UserGroup_GetNextNumber;
