-- dbo.usp_BusinessUnit_List
-- @ActiveOnly = 0: return all business units (Admin / filters).
-- @ActiveOnly = 1: return only rows where IsActive = 1 (e.g. client pick lists).

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

CREATE OR ALTER PROCEDURE dbo.usp_BusinessUnit_List
    @ActiveOnly BIT = 0
AS
BEGIN
    SET NOCOUNT ON;
    SELECT BusinessUnitID, BUName, BUDescription, IsActive, CreatedDate, ModifiedDate
    FROM dbo.tBusinessUnit
    WHERE @ActiveOnly = 0 OR IsActive = 1
    ORDER BY BUName;
END;
GO
