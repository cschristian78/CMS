-- dbo.usp_BusinessClass_Delete
-- Removes a Business Class by BCID. Refuses to delete when the class is
-- still referenced by any client (dbo.tClient.BCID), preserving referential
-- integrity. Mirrors the conventions of dbo.usp_BusinessUnit_Delete.

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

CREATE OR ALTER PROCEDURE dbo.usp_BusinessClass_Delete
    @BCID INT
AS
BEGIN
    SET NOCOUNT ON;

    IF @BCID IS NULL OR @BCID <= 0
    BEGIN
        RAISERROR ('A valid BCID is required to delete a Business Class.', 16, 1);
        RETURN;
    END

    IF EXISTS (SELECT 1 FROM dbo.tClient WHERE BCID = @BCID)
    BEGIN
        RAISERROR ('Cannot delete: this Business Class is still assigned to one or more clients.', 16, 1);
        RETURN;
    END

    DELETE FROM dbo.tBusinessClass WHERE BCID = @BCID;

    SELECT CAST(@@ROWCOUNT AS INT) AS RowsAffected;
END
GO
