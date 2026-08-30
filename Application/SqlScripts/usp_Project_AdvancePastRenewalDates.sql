/*
  Move renewal dates back one year for projects whose RenewalDate is before today.
  Recalculates DueDate as two calendar months before the new RenewalDate.
  Projects with RenewalDate on or after today are unchanged.

  Example: RenewalDate 2027-07-01 -> 2026-07-01, DueDate -> 2026-05-01

  use bms_cms
*/
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;

DECLARE @Today DATE = CAST(GETDATE() AS DATE);

BEGIN TRANSACTION;

UPDATE p
SET
    RenewalDate = DATEADD(YEAR, -1, p.RenewalDate),
    DueDate = DATEADD(MONTH, -2, DATEADD(YEAR, -1, p.RenewalDate)),
    ModifiedDate = SYSUTCDATETIME()
FROM dbo.tProject p
--WHERE p.RenewalDate IS NOT NULL
 -- AND p.RenewalDate < @Today;

SELECT @@ROWCOUNT AS ProjectsUpdated;

IF OBJECT_ID(N'dbo.usp_Create_vProject_Current', N'P') IS NOT NULL
BEGIN
    EXEC dbo.usp_Create_vProject_Current;
END

COMMIT TRANSACTION;

select * from tproject
