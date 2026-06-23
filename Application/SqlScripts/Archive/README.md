# CMS Phase I SQL Package

Generated from `/mnt/data/CMS ERD.xlsx` on 2026-05-13 01:19 UTC.

## Workbook tabs reviewed

- Handoff
- ERD v2
- Stored Procedures
- Seed Data
- Delete Policy
- T-SQL Templates
- Required SPs
- Test Scenarios
- Sheet1

## Execution order

1. `01_CMS_PhaseI_DDL.sql`
2. `02_CMS_PhaseI_Views.sql`
3. `03_CMS_PhaseI_SeedData.sql`
4. `04_CMS_PhaseI_StoredProcedures.sql`
5. Optional: `05_CMS_PhaseI_SmokeTests.sql`

## Notes

- The package follows ERD v2 from the workbook.
- Client is modeled as the top business entity.
- Project is modeled as the operational work entity beneath Client.
- Client BusinessUnit is resolved through BusinessClass in `vClient_WithBU`, consistent with the ERD v2 notes.
- Soft delete is implemented for Client, Project, User, UserGroup, and Service.
- Lookup tables use `IsActive` rather than physical delete.
- Revenue is modeled as history in `tProjectRevenue`; current revenue is exposed through `vProject_Current`.
- Stored procedures implement all procedures listed in the Required SPs tab.
