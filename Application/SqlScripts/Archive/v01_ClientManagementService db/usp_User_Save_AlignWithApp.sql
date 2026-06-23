-- =============================================================================
-- Align dbo.usp_User_Insert / usp_User_Update / usp_User_Copy with the app
-- =============================================================================
-- Fixes: "Procedure or function 'usp_User_Insert' has too many arguments specified."
-- Cause: ClientManagementSystem always sends @PostalCode (and parameter widths/types
--        must match). Older Phase I procs omitted @PostalCode and used a different
--        parameter set.
--
-- Run after ensuring dbo.tUser has a Department column (not legacy typo Departement),
-- and add PostalCode if missing.
-- =============================================================================

IF COL_LENGTH('dbo.tUser', 'PostalCode') IS NULL
    ALTER TABLE dbo.tUser ADD PostalCode NVARCHAR(25) NULL;
GO

CREATE OR ALTER PROCEDURE dbo.usp_User_Insert
    @FirstName NVARCHAR(100) = NULL,
    @LastName NVARCHAR(100) = NULL,
    @Email NVARCHAR(100) = NULL,
    @Phone NVARCHAR(20) = NULL,
    @Company NVARCHAR(50) = NULL,
    @Address NVARCHAR(50) = NULL,
    @City NVARCHAR(25) = NULL,
    @State NVARCHAR(25) = NULL,
    @PostalCode NVARCHAR(25) = NULL,
    @Country NVARCHAR(25) = NULL,
    @Department NVARCHAR(100) = NULL,
    @Title NVARCHAR(100) = NULL,
    @StatusID INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF @StatusID IS NULL
    BEGIN
        SELECT @StatusID = StatusID FROM dbo.tStatus WHERE StatusCode = 'A';
    END;
    IF @StatusID IS NULL
        SET @StatusID = 1;

    INSERT INTO dbo.tUser
    (
        FirstName,
        LastName,
        Email,
        Phone,
        Company,
        Address,
        City,
        State,
        PostalCode,
        Country,
        Department,
        Title,
        StatusID
    )
    VALUES
    (
        @FirstName,
        @LastName,
        @Email,
        @Phone,
        @Company,
        @Address,
        @City,
        @State,
        @PostalCode,
        @Country,
        @Department,
        @Title,
        @StatusID
    );

    SELECT CAST(SCOPE_IDENTITY() AS INT) AS UserID;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_User_Update
    @UserID INT,
    @FirstName NVARCHAR(100) = NULL,
    @LastName NVARCHAR(100) = NULL,
    @Email NVARCHAR(100) = NULL,
    @Phone NVARCHAR(20) = NULL,
    @Company NVARCHAR(50) = NULL,
    @Address NVARCHAR(50) = NULL,
    @City NVARCHAR(25) = NULL,
    @State NVARCHAR(25) = NULL,
    @PostalCode NVARCHAR(25) = NULL,
    @Country NVARCHAR(25) = NULL,
    @Department NVARCHAR(100) = NULL,
    @Title NVARCHAR(100) = NULL,
    @StatusID INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.tUser
    SET
        FirstName = @FirstName,
        LastName = @LastName,
        Email = @Email,
        Phone = @Phone,
        Company = @Company,
        Address = @Address,
        City = @City,
        State = @State,
        PostalCode = @PostalCode,
        Country = @Country,
        Department = @Department,
        Title = @Title,
        StatusID = @StatusID
    WHERE UserID = @UserID;

    SELECT @@ROWCOUNT AS RowsAffected;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_User_Copy
    @SourceUserID INT,
    @NewEmail NVARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.tUser
    (
        FirstName,
        LastName,
        Title,
        Email,
        Phone,
        Department,
        Company,
        Address,
        City,
        State,
        PostalCode,
        Country,
        StatusID
    )
    SELECT
        FirstName,
        LastName,
        Title,
        @NewEmail,
        Phone,
        Department,
        Company,
        Address,
        City,
        State,
        PostalCode,
        Country,
        StatusID
    FROM dbo.tUser
    WHERE UserID = @SourceUserID;

    SELECT CAST(SCOPE_IDENTITY() AS INT) AS UserID;
END;
GO
