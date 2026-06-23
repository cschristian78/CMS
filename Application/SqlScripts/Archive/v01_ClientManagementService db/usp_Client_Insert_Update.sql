/*
    Align dbo.usp_Client_Insert / dbo.usp_Client_Update with slim dbo.tClient:
    ClientName, ClientShortName, StatusID, BUID (no BCID, MGA, Program, AccountExecutive, AnalyticsLead).

    Deploy after dbo.tClient matches these columns. The ClientManagement app calls these procedures from ClientRepository.
*/

SET ANSI_NULLS ON;
GO

SET QUOTED_IDENTIFIER ON;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Client_Insert
    @ClientName NVARCHAR(100),
    @ClientShortName NVARCHAR(100) = NULL,
    @StatusID INT,
    @BUID INT
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.tClient (ClientName, ClientShortName, StatusID, BUID)
    VALUES (@ClientName, @ClientShortName, @StatusID, @BUID);

    SELECT CAST(SCOPE_IDENTITY() AS INT) AS ClientID;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Client_Update
    @ClientID INT,
    @ClientName NVARCHAR(100),
    @ClientShortName NVARCHAR(100) = NULL,
    @StatusID INT,
    @BUID INT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.tClient
    SET
        ClientName = @ClientName,
        ClientShortName = @ClientShortName,
        StatusID = @StatusID,
        BUID = @BUID
    WHERE ClientID = @ClientID;

    SELECT CAST(@@ROWCOUNT AS INT) AS RowsAffected;
END;
GO
