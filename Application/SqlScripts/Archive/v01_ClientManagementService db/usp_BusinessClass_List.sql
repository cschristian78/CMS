-- dbo.usp_BusinessClass_List
-- Returns rows from dbo.tBusinessClass with parent BU display fields.
-- @IncludeInactive = 1 (default) includes classes under inactive business units (Admin / full directory).

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

CREATE OR ALTER PROCEDURE dbo.usp_BusinessClass_List
    @BusinessUnitID INT = NULL,
    @IncludeInactive BIT = 1
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        bc.BusinessClassID,
        bc.BCName,
        bc.BCDescription,
        bc.BusinessUnitID,
        bu.BUName,
        bu.IsActive AS BusinessUnitIsActive,
        bc.CreatedDate,
        bc.ModifiedDate
    FROM dbo.tBusinessClass bc
    INNER JOIN dbo.tBusinessUnit bu ON bc.BusinessUnitID = bu.BusinessUnitID
    WHERE (@BusinessUnitID IS NULL OR bc.BusinessUnitID = @BusinessUnitID)
      AND (@IncludeInactive = 1 OR bu.IsActive = 1)
    ORDER BY bu.BUName, bc.BCName;
END;
GO
