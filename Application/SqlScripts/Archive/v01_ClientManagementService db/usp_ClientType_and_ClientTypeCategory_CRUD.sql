/*
    dbo.tClientType & dbo.tClientTypeCategory — CRUD + GetActive + SetStatus

    Naming (per table):
      GetAll, GetActive, GetByID, Insert, Update, SetStatus, Delete

    Assumed columns:
      tClientType:          ClientTypeID (IDENTITY PK), ClientTypeName, ClientTypeDescription (nullable), IsActive (BIT)
      tClientTypeCategory:  CTCID (IDENTITY PK), CTCName, CTCDescription (nullable), ClientID (nullable), IsActive (BIT)

    Run the ALTER block once if IsActive / ClientID are missing. Adjust types if your database differs.
*/

SET ANSI_NULLS ON;
GO

SET QUOTED_IDENTIFIER ON;
GO

/* ---- Optional: extend tables for active flag & optional client scope ---- */
IF COL_LENGTH(N'dbo.tClientType', N'IsActive') IS NULL
BEGIN
    ALTER TABLE dbo.tClientType ADD IsActive BIT NOT NULL
        CONSTRAINT DF_tClientType_IsActive DEFAULT (1);
END;
GO

IF COL_LENGTH(N'dbo.tClientTypeCategory', N'IsActive') IS NULL
BEGIN
    ALTER TABLE dbo.tClientTypeCategory ADD IsActive BIT NOT NULL
        CONSTRAINT DF_tClientTypeCategory_IsActive DEFAULT (1);
END;
GO

IF COL_LENGTH(N'dbo.tClientTypeCategory', N'ClientID') IS NULL
BEGIN
    ALTER TABLE dbo.tClientTypeCategory ADD ClientID INT NULL;
END;
GO

/* ============================================================
   dbo.tClientType
   ============================================================ */

CREATE OR ALTER PROCEDURE dbo.usp_ClientType_GetAll
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        ct.ClientTypeID,
        ct.ClientTypeName,
        ct.ClientTypeDescription,
        ct.IsActive
    FROM dbo.tClientType AS ct
    ORDER BY ct.ClientTypeName, ct.ClientTypeID;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_ClientType_GetActive
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        ct.ClientTypeID,
        ct.ClientTypeName,
        ct.ClientTypeDescription,
        ct.IsActive
    FROM dbo.tClientType AS ct
    WHERE ct.IsActive = 1
    ORDER BY ct.ClientTypeName, ct.ClientTypeID;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_ClientType_GetByID
    @ClientTypeID INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        ct.ClientTypeID,
        ct.ClientTypeName,
        ct.ClientTypeDescription,
        ct.IsActive
    FROM dbo.tClientType AS ct
    WHERE ct.ClientTypeID = @ClientTypeID;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_ClientType_Insert
    @ClientTypeName NVARCHAR(100),
    @ClientTypeDescription NVARCHAR(1000) = NULL,
    @IsActive BIT = 1
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.tClientType (ClientTypeName, ClientTypeDescription, IsActive)
    VALUES (@ClientTypeName, @ClientTypeDescription, @IsActive);

    SELECT CAST(SCOPE_IDENTITY() AS INT) AS ClientTypeID;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_ClientType_Update
    @ClientTypeID INT,
    @ClientTypeName NVARCHAR(100),
    @ClientTypeDescription NVARCHAR(1000) = NULL,
    @IsActive BIT = 1
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.tClientType
    SET
        ClientTypeName = @ClientTypeName,
        ClientTypeDescription = @ClientTypeDescription,
        IsActive = @IsActive
    WHERE ClientTypeID = @ClientTypeID;

    SELECT CAST(@@ROWCOUNT AS INT) AS RowsAffected;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_ClientType_SetStatus
    @ClientTypeID INT,
    @IsActive BIT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.tClientType
    SET IsActive = @IsActive
    WHERE ClientTypeID = @ClientTypeID;

    SELECT CAST(@@ROWCOUNT AS INT) AS RowsAffected;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_ClientType_Delete
    @ClientTypeID INT
AS
BEGIN
    SET NOCOUNT ON;

    DELETE FROM dbo.tClientType
    WHERE ClientTypeID = @ClientTypeID;

    SELECT CAST(@@ROWCOUNT AS INT) AS RowsAffected;
END;
GO

/* ============================================================
   dbo.tClientTypeCategory
   ============================================================ */

CREATE OR ALTER PROCEDURE dbo.usp_ClientTypeCategory_GetAll
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        cat.CTCID,
        cat.CTCName,
        cat.CTCDescription,
        cat.ClientID,
        cat.IsActive,
        c.ClientName,
        c.ClientShortName
    FROM dbo.tClientTypeCategory AS cat
    LEFT JOIN dbo.tClient AS c ON c.ClientID = cat.ClientID
    ORDER BY cat.CTCName, cat.CTCID;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_ClientTypeCategory_GetActive
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        cat.CTCID,
        cat.CTCName,
        cat.CTCDescription,
        cat.ClientID,
        cat.IsActive,
        c.ClientName,
        c.ClientShortName
    FROM dbo.tClientTypeCategory AS cat
    LEFT JOIN dbo.tClient AS c ON c.ClientID = cat.ClientID
    WHERE cat.IsActive = 1
    ORDER BY cat.CTCName, cat.CTCID;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_ClientTypeCategory_GetByID
    @CTCID INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        cat.CTCID,
        cat.CTCName,
        cat.CTCDescription,
        cat.ClientID,
        cat.IsActive,
        c.ClientName,
        c.ClientShortName
    FROM dbo.tClientTypeCategory AS cat
    LEFT JOIN dbo.tClient AS c ON c.ClientID = cat.ClientID
    WHERE cat.CTCID = @CTCID;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_ClientTypeCategory_Insert
    @CTCName NVARCHAR(100),
    @CTCDescription NVARCHAR(1000) = NULL,
    @ClientID INT = NULL,
    @IsActive BIT = 1
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.tClientTypeCategory (CTCName, CTCDescription, ClientID, IsActive)
    VALUES (@CTCName, @CTCDescription, @ClientID, @IsActive);

    SELECT CAST(SCOPE_IDENTITY() AS INT) AS CTCID;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_ClientTypeCategory_Update
    @CTCID INT,
    @CTCName NVARCHAR(100),
    @CTCDescription NVARCHAR(1000) = NULL,
    @ClientID INT = NULL,
    @IsActive BIT = 1
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.tClientTypeCategory
    SET
        CTCName = @CTCName,
        CTCDescription = @CTCDescription,
        ClientID = @ClientID,
        IsActive = @IsActive
    WHERE CTCID = @CTCID;

    SELECT CAST(@@ROWCOUNT AS INT) AS RowsAffected;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_ClientTypeCategory_SetStatus
    @CTCID INT,
    @IsActive BIT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.tClientTypeCategory
    SET IsActive = @IsActive
    WHERE CTCID = @CTCID;

    SELECT CAST(@@ROWCOUNT AS INT) AS RowsAffected;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_ClientTypeCategory_Delete
    @CTCID INT
AS
BEGIN
    SET NOCOUNT ON;

    DELETE FROM dbo.tClientTypeCategory
    WHERE CTCID = @CTCID;

    SELECT CAST(@@ROWCOUNT AS INT) AS RowsAffected;
END;
GO
