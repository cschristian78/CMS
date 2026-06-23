# Phase I SQL Delivery - Client Management System

## Purpose

This package delivers Phase I of the Client Management System database work for SQL Server.

Target database:

```text
BMS_ProjectManagementSystem
```

## Delivered files

| File | Purpose |
|---|---|
| `00_RunAll_PhaseI.sql` | Master SQLCMD deployment script. Runs all Phase I scripts in order. |
| `PhaseI_FullDeployment_Combined.sql` | Single-file version for SSMS/Azure Data Studio execution. |
| `01_CreateDatabase.sql` | Creates `BMS_ProjectManagementSystem` if it does not exist. |
| `02_CreateTableStoredProcedures.sql` | Creates idempotent table-creation stored procedures for all required tables. |
| `03_ExecuteCreateTableProcedures.sql` | Executes the table-creation stored procedures. |
| `04_CreateForeignKeysAndIndexes.sql` | Adds foreign keys, lookup uniqueness, and query-supporting indexes. |
| `05_SeedLookupData.sql` | Seeds required statuses, initial segments, project types, services, and user roles. |
| `06_CreateCrudStoredProcedures.sql` | Creates CRUD/list stored procedures for clients, projects, statuses, segments, project types, services, users, roles, and groups. |
| `07_CreateDashboardStoredProcedure.sql` | Creates dashboard stored procedure for client counts by segment. |
| `08_CreateDatabaseSecurityRole.sql` | Creates least-privilege app role `app_CMS_Execute` and grants stored procedure execution. |
| `09_VerificationQueries.sql` | Read-only verification queries after deployment. |

## How to deploy

Option A - from a command prompt on a machine with SQL Server command-line tools installed, open this folder and run:

```cmd
sqlcmd -S vmus-sql-099 -E -i 00_RunAll_PhaseI.sql
```

Use `-E` for Windows Integrated Security. This avoids putting SQL usernames or passwords in the command or script.

To deploy to the secondary server:

```cmd
sqlcmd -S VMUS-DPS-01 -E -i 00_RunAll_PhaseI.sql
```

Option B - open `PhaseI_FullDeployment_Combined.sql` in SQL Server Management Studio or Azure Data Studio and execute it while connected to the target SQL Server.

## Major implementation notes

1. `tProject` now includes an internal `ProjectID INT IDENTITY(1,1) PRIMARY KEY` because the requirements define `tProjectOperations.ProjectID`, but the original `tProject` structure only included `ClientProjectID`. `ClientProjectID` is retained as the external Orchestrator project identifier.
2. `tUser.StatusID` and `tUserRole.StatusID` are implemented as `INT` because the requirements did not specify a data type and both should reference `tStatus(StatusID)`.
3. The scripts use `CREATE OR ALTER PROCEDURE` and existence checks so they can be safely rerun.
4. The application execution role is created as `app_CMS_Execute`; add the real service account or AD group to this role after deployment.
5. Direct username/password storage is not included in these scripts. Use Windows Integrated Security, encrypted application configuration, or a managed secret store in the application layer.

## Deployment order

The master script executes the following order:

1. Create database.
2. Create table-creation stored procedures.
3. Execute table-creation stored procedures.
4. Add foreign keys and indexes.
5. Seed lookup data.
6. Create CRUD stored procedures.
7. Create dashboard stored procedure.
8. Create database execution role.
9. Run verification queries.

## Post-deployment checks

After deployment, confirm that:

- All required tables exist.
- Status values `A = Active` and `I = Inactive` exist.
- CRUD stored procedures exist.
- `usp_Dashboard_ClientCountBySegment` executes successfully.
- The application service account or AD group has been added to `app_CMS_Execute`.
