-- Align dbo.usp_Project_Insert / dbo.usp_Project_Update with Phase II client (no ClientProjectID parameter).
-- Existing dbo.tProject.ClientProjectID column is unchanged; new inserts leave it NULL; updates do not modify it.

CREATE OR ALTER PROCEDURE dbo.usp_Project_Insert
    @ClientID INT,
    @UserGroupNumber INT = NULL,
    @ProjectName NVARCHAR(100),
    @ProjectTypeID INT,
    @Revenue FLOAT = NULL,
    @RenewalDate DATETIME2(0) = NULL,
    @DueDate DATETIME2(0) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.tProject
    (
        ClientID,
        UserGroupNumber,
        ProjectName,
        ProjectTypeID,
        Revenue,
        RenewalDate,
        DueDate
    )
    VALUES
    (
        @ClientID,
        @UserGroupNumber,
        @ProjectName,
        @ProjectTypeID,
        @Revenue,
        @RenewalDate,
        @DueDate
    );

    SELECT CAST(SCOPE_IDENTITY() AS INT) AS ProjectID;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Project_Update
    @ProjectID INT,
    @ClientID INT,
    @UserGroupNumber INT = NULL,
    @ProjectName NVARCHAR(100),
    @ProjectTypeID INT,
    @Revenue FLOAT = NULL,
    @RenewalDate DATETIME2(0) = NULL,
    @DueDate DATETIME2(0) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.tProject
    SET
        ClientID = @ClientID,
        UserGroupNumber = @UserGroupNumber,
        ProjectName = @ProjectName,
        ProjectTypeID = @ProjectTypeID,
        Revenue = @Revenue,
        RenewalDate = @RenewalDate,
        DueDate = @DueDate
    WHERE ProjectID = @ProjectID;

    SELECT @@ROWCOUNT AS RowsAffected;
END;
GO
