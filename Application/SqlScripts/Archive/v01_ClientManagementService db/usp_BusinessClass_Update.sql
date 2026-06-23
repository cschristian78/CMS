-- dbo.usp_BusinessClass_Update
-- Updates a single dbo.tBusinessClass row by BCID and returns the rowcount.
-- Mirrors the conventions of dbo.usp_BusinessUnit_Update.

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

CREATE OR ALTER PROCEDURE dbo.usp_BusinessClass_Update
    @BCID           INT,
    @BUID           INT,
    @BCName         VARCHAR(100),
    @BCDescription  VARCHAR(1000) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF @BCID IS NULL OR @BCID <= 0
    BEGIN
        RAISERROR ('A valid BCID is required to update a Business Class.', 16, 1);
        RETURN;
    END

    IF @BUID IS NULL OR @BUID <= 0
    BEGIN
        RAISERROR ('A valid BUID is required to update a Business Class.', 16, 1);
        RETURN;
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.tBusinessUnit WHERE BUID = @BUID)
    BEGIN
        RAISERROR ('Parent Business Unit (BUID = %d) was not found.', 16, 1, @BUID);
        RETURN;
    END

    UPDATE dbo.tBusinessClass
       SET BUID          = @BUID,
           BCName        = LTRIM(RTRIM(@BCName)),
           BCDescription = NULLIF(LTRIM(RTRIM(@BCDescription)), '')
     WHERE BCID = @BCID;

    SELECT CAST(@@ROWCOUNT AS INT) AS RowsAffected;
END
GO
