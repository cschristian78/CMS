-- Align dbo.usp_Project_Insert / dbo.usp_Project_Update / dbo.usp_Project_GetByID with the Phase II app:
-- @CreatedDate (datetime2), @StatusID (int, default 1 = Active).
-- Deploy after tProject_StatusID.sql if StatusID column was missing.

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Project_GetByID
    @ProjectID INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        p.ProjectID,
        p.ClientProjectID,
        p.ClientID,
        c.ClientName,
        p.UserGroupNumber,
        p.ProjectName,
        p.ProjectTypeID,
        pt.ProjectTypeCode,
        pt.ProjectTypeDescription,
        p.Revenue,
        p.RenewalDate,
        p.DueDate,
        p.CreatedDate,
        p.StatusID
    FROM dbo.tProject AS p
    LEFT JOIN dbo.tClient AS c ON p.ClientID = c.ClientID
    LEFT JOIN dbo.tProjectType AS pt ON p.ProjectTypeID = pt.ProjectTypeID
    WHERE p.ProjectID = @ProjectID;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_Project_Insert
    @ClientID INT,
    @UserGroupNumber INT = NULL,
    @ProjectName NVARCHAR(100),
    @ProjectTypeID INT,
    @Revenue FLOAT = NULL,
    @RenewalDate DATETIME2(0) = NULL,
    @DueDate DATETIME2(0) = NULL,
    @CreatedDate DATETIME2(0) = NULL,
    @StatusID INT = 1
AS
BEGIN
    SET NOCOUNT ON;

    IF @StatusID IS NULL OR @StatusID <= 0
        SET @StatusID = 1;

    INSERT INTO dbo.tProject
    (
        ClientID,
        UserGroupNumber,
        ProjectName,
        ProjectTypeID,
        Revenue,
        RenewalDate,
        DueDate,
        CreatedDate,
        StatusID
    )
    VALUES
    (
        @ClientID,
        @UserGroupNumber,
        @ProjectName,
        @ProjectTypeID,
        @Revenue,
        @RenewalDate,
        @DueDate,
        COALESCE(@CreatedDate, SYSUTCDATETIME()),
        @StatusID
    );

    SELECT CAST(SCOPE_IDENTITY() AS INT) AS ProjectID;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_Project_Update
    @ProjectID INT,
    @ClientID INT,
    @UserGroupNumber INT = NULL,
    @ProjectName NVARCHAR(100),
    @ProjectTypeID INT,
    @Revenue FLOAT = NULL,
    @RenewalDate DATETIME2(0) = NULL,
    @DueDate DATETIME2(0) = NULL,
    @CreatedDate DATETIME2(0) = NULL,
    @StatusID INT = 1
AS
BEGIN
    SET NOCOUNT ON;

    IF @StatusID IS NULL OR @StatusID <= 0
        SET @StatusID = 1;

    UPDATE dbo.tProject
    SET
        ClientID = @ClientID,
        UserGroupNumber = @UserGroupNumber,
        ProjectName = @ProjectName,
        ProjectTypeID = @ProjectTypeID,
        Revenue = @Revenue,
        RenewalDate = @RenewalDate,
        DueDate = @DueDate,
        CreatedDate = COALESCE(@CreatedDate, CreatedDate),
        StatusID = @StatusID
    WHERE ProjectID = @ProjectID;

    SELECT @@ROWCOUNT AS RowsAffected;
END
GO
