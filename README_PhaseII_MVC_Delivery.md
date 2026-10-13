# Phase II MVC UI Delivery - Client Management System

This package contains the Phase II ASP.NET Core MVC implementation for the Client Management System.

## Contents

```text
ClientManagementSystem/
  Api/                JSON APIs consumed by the SPA (home summary, client/project list, etc.)
  ClientApp/          React SPA (Vite): routing, pages including Home client/project grid
  Controllers/        MVC controllers for Home, Clients, Projects, Admin, Dashboard
  Data/               SQL stored procedure repositories, text queries, connection factory
  Models/             Domain models matching the Phase I SQL tables
  Security/           Encrypted server/database/credential configuration service
  Services/           Business/application services used by controllers and APIs
  SqlScripts/         Incremental Phase II SQL scripts (deploy to BMS_CMS as needed)
  ViewModels/         MVC form and page view models
  Views/              Razor views and shared layout
  wwwroot/            Built SPA assets, CSS, and JavaScript
  App_Data/           Runtime folder for encrypted connection settings and Data Protection keys
```

## Technology choices

- ASP.NET Core MVC with JSON API controllers under `Api/`, C#, Razor, HTML, CSS, and JavaScript.
- React SPA under `ClientApp/` for interactive pages (home dashboard tiles and **`dbo.tClientProjectList`** grid).
- SQL Server backend using the stored procedures delivered in Phase I, plus **`dbo.usp_RefreshClientProjectList`** for the materialized client/project list (see below).
- ADO.NET through `Microsoft.Data.SqlClient`.
- No hardcoded connection string in `appsettings.json`.
- Server, database, username, and password values are saved through ASP.NET Core Data Protection into `App_Data/connectionsettings.protected.json`.

## Default database configuration

The application defaults to:

```text
Server: vmus-sql-099
Database: BMS_CMS
Username: null
Password: null
Security: Windows Integrated Security
```

The Admin > Server tab restricts server choices to:

```text
vmus-sql-099
VMUS-DPS-01
MoodysDB
VeriskCurrent
VeriskPrevious
```

## How to run locally

1. Install the .NET 8 SDK or open the project in Visual Studio 2022.
2. Confirm that the Phase I database has been deployed, including **`dbo.usp_RefreshClientProjectList`** (see Phase I README or run `SqlScripts\usp_RefreshClientProjectList.sql` if your Phase I drop omits it).
3. Confirm that the Windows account running the app can execute the stored procedures in `BMS_CMS` (Phase I **`app_CMS_Execute`** role).
4. From the project folder, run:

```cmd
dotnet restore
dotnet build
dotnet run
```

5. Open the URL shown by `dotnet run`, or use the launch profile:

```text
https://localhost:7044
http://localhost:5044
```

## First application checks

1. Open the landing page and confirm the tiles display:
   - New Project
   - New Client
   - Dashboard
2. Confirm the File menu includes:
   - New Project
   - New Client
   - Dashboard
   - Admin
3. Open Admin > Server and test the default SQL connection.
4. Open Admin > Status and confirm Active/Inactive statuses load.
5. Open Admin > Segment and confirm segment rows load.
6. Open Admin > Project Types and confirm project types load.
7. Open New Client and save a client.
8. Open New Project and save a project tied to a client and project type.
9. Open the Home route (landing SPA): confirm **Client / project list** loads without API errors. The UI calls **`GET /api/home/client-project-list?refresh=true`** on first load, which runs **`dbo.usp_RefreshClientProjectList`** then reads **`dbo.tClientProjectList`**.

## Client / project list (home grid)

The React Home page depends on:

| Piece | Description |
| --- | --- |
| **`dbo.usp_RefreshClientProjectList`** | Called when `refresh=true` (default on initial load). Rebuilds **`dbo.tClientProjectList`**. |
| **`dbo.tClientProjectList`** | Queried as **`SELECT *`** with optional filters (segment, status, client). Expect columns such as **`ProjectID`**, **`ClientID`**, **`ClientName`**, **`StatusName`**, plus dynamic pivot columns per **`UserRoleName`**. |
| **`dbo.tUserRole`** | Must define at least one non-blank **`UserRoleName`** or the refresh procedure fails—see Phase I README. |

Related HTTP endpoints:

- **`GET /api/home/client-project-list`** — list payload used by the home grid (also duplicated under **`GET /api/dashboard/client-project-list`** with the same behavior).
- **`GET /api/dashboard/client-project-list/options`** — segment, status, and client names for filters.
- **`GET /api/home/summary`** — summary tiles; filtered mode joins **`dbo.tClientProjectList`**.

If the API returns *Could not refresh or read dbo.tClientProjectList*, verify the procedure exists, completes in SSMS, seeded **`tUserRole`** rows exist, and the app connection targets **`BMS_CMS`**.

## Phase II incremental SQL (`SqlScripts`)

Scripts in **`ClientManagementSystem\SqlScripts\`** apply **on top of** Phase I. Deploy them in SSMS/Azure Data Studio against **`BMS_CMS`** when needed.

| Script | Purpose |
| --- | --- |
| **`usp_RefreshClientProjectList.sql`** | **`CREATE OR ALTER`** for **`dbo.usp_RefreshClientProjectList`** (matches Phase I combined deployment logic; use to patch or redeploy the proc only). |
| **`PhaseII_tProjectService.sql`** | Phase II project/service schema and related procedures. |
| **`Project_AddDueDate.sql`** | Adds due-date column support where applicable. |
| **`usp_Project_Remove_ClientProjectID_Param.sql`** | Procedure signature adjustment for project APIs. |
| **`usp_User_Delete.sql`** | User delete procedure update. |
| **`usp_UserGroup_GetNextNumber.sql`** | User group numbering helper. |
| **`tUser_Department_Title.sql`** | User table columns for department/title (incremental). |

Uncomment or add **`USE [BMS_CMS];`** in each script when executing manually.

## Stored procedure dependencies

The MVC code is wired to the Phase I stored procedure names, including:

```text
usp_Client_GetTop25
usp_Client_GetByID
usp_Client_Insert
usp_Client_Update
usp_Client_SetStatus
usp_Project_GetByID
usp_Project_Insert
usp_Project_Update
usp_Status_GetAll
usp_Status_Insert
usp_Status_Update
usp_Status_Delete
usp_Segment_GetAll
usp_Segment_GetTop25
usp_Segment_Insert
usp_Segment_Update
usp_Segment_Delete
usp_ProjectType_GetAll
usp_ProjectType_Insert
usp_ProjectType_Update
usp_ProjectType_Delete
usp_User_GetTop25
usp_User_GetBMSUsers
usp_User_Insert
usp_User_Update
usp_User_Copy
usp_User_Deactivate
usp_User_Delete
usp_UserRole_GetAll
usp_UserRole_Insert
usp_UserRole_Update
usp_UserRole_DeleteOrDeactivate
usp_UserGroup_GetAll
usp_UserGroup_GetTop25Groups
usp_UserGroup_Insert
usp_UserGroup_Update
usp_UserGroup_Delete
usp_Dashboard_ClientCountBySegment
usp_RefreshClientProjectList
```

## Security notes

- Do not commit `App_Data/connectionsettings.protected.json`.
- Do not commit `App_Data/DataProtectionKeys/`.
- Grant the application identity execute permission through the Phase I `app_CMS_Execute` role.
- Avoid granting the web application identity direct table access. Note that **`usp_RefreshClientProjectList`** runs as **`dbo`** and internally **`DROP`s / recreates `dbo.tClientProjectList`**; callers only need **`EXECUTE`** on the procedure, not ad hoc DDL rights on the account.
- Add authentication and authorization before production deployment. The AdminController is isolated so an `[Authorize]` attribute or policy can be applied when the firm's identity provider is selected.

## Notes about current package

- The project is ready for Visual Studio/.NET SDK build, but this environment did not have the `dotnet` CLI available for a live compilation test.
- Dashboard view is included so the dashboard tile has a functional destination. The richer visualization work can still be expanded in Phase III.
- The `tClient.AccountExecutive` and `tClient.AnalyticsLead` fields are `nvarchar(25)` in Phase I, so the UI stores a selected user's display name truncated to 25 characters.

## Real-time updates (SignalR)

File Notes on a project update live for other signed-in users who have that project's File Notes tab open. Posting, editing, or deleting a note calls `IRealtimeNotifier`, which pushes an `update` message through `UpdatesHub` at `/hubs/updates`. The React client (`@microsoft/signalr`) refreshes the thread. A short poll runs only while the hub is disconnected.

The hub requires the existing cookie sign-in. On connect it adds the connection to a group derived from the server user id. `JoinProject` accepts a project id only after the server confirms the project exists; the client cannot choose an arbitrary group name. Anonymous calls to `/hubs/updates` are rejected.

### Transports

The client does not set `skipNegotiation` and does not force WebSockets. SignalR negotiates the best transport. Until Group IT enables the IIS WebSocket Protocol feature, the browser console (`[SignalR] transport in use:`) shows `ServerSentEvents` or `LongPolling`. After that feature is enabled, the same code uses WebSockets automatically. No application change is required.

### web.config

`Application/web.config` turns off static and dynamic compression for the `hubs` path so IIS does not buffer the Server-Sent Events stream. That setting does not depend on the WebSocket module.

### How to verify

1. Sign in as two users and open the same project's File Notes tab. The status line should read **Live updates connected**.
2. In the browser console, confirm a line like `[SignalR] transport in use: ServerSentEvents` (or `LongPolling` / `WebSockets`).
3. Post a note from one browser. The other thread should update within about a second without a page refresh.
4. Call `/hubs/updates` without a session. The negotiate request should be rejected (401).
