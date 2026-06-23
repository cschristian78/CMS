-- dbo.usp_BusinessClass_Insert
-- Inserts a row into dbo.tBusinessClass and returns the new BCID.
-- Mirrors the conventions of dbo.usp_BusinessUnit_Insert.

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

CREATE OR ALTER PROCEDURE dbo.usp_BusinessClass_Insert
    @BUID           INT,
    @BCName         VARCHAR(100),
    @BCDescription  VARCHAR(1000) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF @BUID IS NULL OR @BUID <= 0
    BEGIN
        RAISERROR ('A valid BUID is required to create a Business Class.', 16, 1);
        RETURN;
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.tBusinessUnit WHERE BUID = @BUID)
    BEGIN
        RAISERROR ('Parent Business Unit (BUID = %d) was not found.', 16, 1, @BUID);
        RETURN;
    END

    INSERT INTO dbo.tBusinessClass (BUID, BCName, BCDescription)
    VALUES (@BUID, LTRIM(RTRIM(@BCName)), NULLIF(LTRIM(RTRIM(@BCDescription)), ''));

    SELECT CAST(SCOPE_IDENTITY() AS INT) AS BCID;
END
GO
