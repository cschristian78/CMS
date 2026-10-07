using System.Globalization;
using ClientManagementSystem.Models;
using Microsoft.Data.SqlClient;

namespace ClientManagementSystem.Data;

/// <summary>Project reads/writes against CMS Phase I v3: <c>tProject</c> header plus
/// <c>tProjectRef</c>, <c>tProjectService</c>, and <c>tProjectUser</c> child rows.</summary>
public sealed class ProjectRepository : IProjectRepository
{
    private const string ProjectSelectSql = """
        SELECT
            p.ProjectID,
            p.ProjectNumber,
            p.ProjectName,
            p.RenewalDate,
            p.DueDate,
            p.CreatedDate,
            p.ModifiedDate,
            IsDeleted = CAST(CASE WHEN p.IsActive = 0 THEN 1 ELSE 0 END AS BIT),
            IsOnline = CAST(ISNULL(p.IsOnline, 0) AS BIT),
            IsFeeBasedRevenue = CAST(ISNULL(p.IsFeeBasedRevenue, 0) AS BIT),
            c.ClientID,
            c.ClientName,
            c.ClientShortName,
            cr.BusinessClassID,
            bc.BusinessClassName,
            bc.BusinessClassDescription,
            cr.BusinessUnitID,
            bu.BusinessUnitName,
            pref.ProjectRefID,
            pref.ProjectTypeID,
            PTCode = pt.ProjectTypeCode,
            PTDescription = pt.ProjectTypeDescription,
            pref.StatusID,
            st.StatusCode,
            st.StatusDescription,
            UGID = ISNULL(pref.UserGroupID, 0),
            UGName = ugPref.UserGroupName
          FROM dbo.tProject AS p
          OUTER APPLY (
              SELECT TOP (1) pr.ProjectRefID, pr.ClientRefID, pr.ProjectTypeID, pr.StatusID, pr.UserGroupID
                FROM dbo.tProjectRef AS pr
               WHERE pr.ProjectID = p.ProjectID AND pr.IsActive = 1
               ORDER BY pr.ProjectRefID
          ) AS pref
          LEFT JOIN dbo.tClientRef AS cr ON cr.ClientRefID = pref.ClientRefID
          LEFT JOIN dbo.tClient AS c ON c.ClientID = cr.ClientID
          LEFT JOIN dbo.tBusinessUnit AS bu ON bu.BusinessUnitID = cr.BusinessUnitID
          LEFT JOIN dbo.tBusinessClass AS bc ON bc.BusinessClassID = cr.BusinessClassID
          LEFT JOIN dbo.tProjectType AS pt ON pt.ProjectTypeID = pref.ProjectTypeID
          LEFT JOIN dbo.tStatus AS st ON st.StatusID = pref.StatusID
          LEFT JOIN dbo.tUserGroup AS ugPref ON ugPref.UserGroupID = pref.UserGroupID
        """;

    private readonly IDbExecutor _db;
    private readonly ISqlConnectionFactory _connections;

    public ProjectRepository(IDbExecutor db, ISqlConnectionFactory connections)
    {
        _db = db;
        _connections = connections;
    }

    public async Task<string> GetNextProjectNumberPreviewAsync(CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT ISNULL(MAX(ProjectID), 0) + 1 AS NextProjectId FROM dbo.tProject;";
        var rows = await _db.QueryTextAsync(sql, static r => r.GetInt32OrDefault("NextProjectId"), null, cancellationToken);
        var next = rows.Count == 0 ? 1 : rows[0];
        return $"PRJ-{next:D6}";
    }

    public Task<IReadOnlyList<Project>> ListAsync(int? clientId = null, int? ugid = null, int? statusId = null, bool includeDeleted = false, CancellationToken cancellationToken = default)
    {
        return _db.QueryAsync("dbo.usp_Project_List", MapProjectHeader,
        [
            SqlParameterFactory.Bit("@ActiveOnly", !includeDeleted)
        ], cancellationToken);
    }

    public async Task<Project?> GetAsync(int projectId, CancellationToken cancellationToken = default)
        => await QueryProjectAsync(projectId, cancellationToken);

    public Task<Project?> GetFullAsync(int projectId, CancellationToken cancellationToken = default)
        => GetAsync(projectId, cancellationToken);

    public Task<int> AddAsync(Project project, CancellationToken cancellationToken = default)
        => SaveProjectAndReplaceServicesAsync(project, [], null, null, cancellationToken);

    public Task<int> UpdateAsync(Project project, CancellationToken cancellationToken = default)
        => SaveProjectAndReplaceServicesAsync(project, [], null, null, cancellationToken);

    public async Task<int> SoftDeleteAsync(int projectId, CancellationToken cancellationToken = default)
    {
        await _db.ExecuteAsync("dbo.usp_Project_SoftDelete",
        [
            SqlParameterFactory.Int("@ProjectID", projectId),
            SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId)
        ], cancellationToken);
        return projectId;
    }

    public async Task<ProjectHardDeleteResult> HardDeleteAsync(int projectId, CancellationToken cancellationToken = default)
    {
        var existing = await GetAsync(projectId, cancellationToken)
            ?? throw new InvalidOperationException($"Project {projectId} was not found.");

        var blobPaths = await _db.QueryTextAsync(
            """
            SELECT pf.BlobPath
              FROM dbo.tProjectFile AS pf
              INNER JOIN dbo.tProjectRef AS pr ON pr.ProjectRefID = pf.ProjectRefID
             WHERE pr.ProjectID = @ProjectID
               AND pf.BlobPath IS NOT NULL
               AND LTRIM(RTRIM(pf.BlobPath)) <> N'';
            """,
            static r => r.TryGetNullableString("BlobPath") ?? string.Empty,
            [SqlParameterFactory.Int("@ProjectID", projectId)],
            cancellationToken);

        await _db.ExecuteAsync(
            "dbo.usp_Project_HardDelete",
            [
                SqlParameterFactory.Int("@ProjectID", projectId),
                SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId)
            ],
            cancellationToken);

        return new ProjectHardDeleteResult
        {
            ProjectID = projectId,
            ProjectNumber = existing.ProjectNumber,
            ProjectName = existing.ProjectName,
            BlobPaths = blobPaths.Where(p => !string.IsNullOrWhiteSpace(p)).ToList()
        };
    }

    public async Task<int> RestoreAsync(int projectId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE dbo.tProject
               SET IsActive = 1,
                   ModifiedDate = SYSUTCDATETIME()
             WHERE ProjectID = @ProjectID;
            """;

        await _db.ExecuteTextAsync(sql, [SqlParameterFactory.Int("@ProjectID", projectId)], cancellationToken);
        return projectId;
    }

    public async Task SetOnlineAsync(int projectId, bool isOnline, CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE dbo.tProject
               SET IsOnline = @IsOnline,
                   ModifiedDate = SYSUTCDATETIME()
             WHERE ProjectID = @ProjectID;
            """;

        await _db.ExecuteTextAsync(sql,
        [
            SqlParameterFactory.Int("@ProjectID", projectId),
            SqlParameterFactory.Bit("@IsOnline", isOnline)
        ], cancellationToken);
    }

    public async Task AssignUserGroupAsync(int projectId, int userGroupId, CancellationToken cancellationToken = default)
    {
        if (projectId <= 0)
        {
            throw new InvalidOperationException("ProjectID is required.");
        }

        if (userGroupId <= 0)
        {
            throw new InvalidOperationException("UserGroupID is required.");
        }

        var projectRefId = await ResolveProjectRefIdForProjectAsync(projectId, cancellationToken);
        const string sql = """
            UPDATE dbo.tProjectRef
               SET UserGroupID = @UserGroupID
             WHERE ProjectRefID = @ProjectRefID
               AND IsActive = 1;
            """;

        await _db.ExecuteTextAsync(
            sql,
            [
                SqlParameterFactory.Int("@ProjectRefID", projectRefId),
                SqlParameterFactory.Int("@UserGroupID", userGroupId),
            ],
            cancellationToken);
    }

    public async Task UnassignUserGroupAsync(int projectId, CancellationToken cancellationToken = default)
    {
        if (projectId <= 0)
        {
            throw new InvalidOperationException("ProjectID is required.");
        }

        var projectRefId = await ResolveProjectRefIdForProjectAsync(projectId, cancellationToken);
        const string sql = """
            UPDATE dbo.tProjectRef
               SET UserGroupID = NULL
             WHERE ProjectRefID = @ProjectRefID
               AND IsActive = 1;
            """;

        await _db.ExecuteTextAsync(
            sql,
            [SqlParameterFactory.Int("@ProjectRefID", projectRefId)],
            cancellationToken);
    }

    public async Task UpdateRenewalDateAsync(int projectId, DateTime renewalDate, CancellationToken cancellationToken = default)
    {
        if (projectId <= 0)
        {
            throw new InvalidOperationException("ProjectID is required.");
        }

        var project = await GetAsync(projectId, cancellationToken)
            ?? throw new InvalidOperationException($"Project {projectId} was not found.");

        await _db.ExecuteAsync("dbo.usp_Project_Update",
        [
            SqlParameterFactory.Int("@ProjectID", projectId),
            SqlParameterFactory.VarChar("@ProjectNumber", 50, project.ProjectNumber),
            SqlParameterFactory.VarChar("@ProjectName", 200, project.ProjectName),
            SqlParameterFactory.Date("@RenewalDate", renewalDate.Date),
            SqlParameterFactory.Date("@DueDate", project.DueDate),
            SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId)
        ], cancellationToken);
    }

    public Task<IReadOnlyList<ProjectRevenue>> GetRevenueHistoryAsync(int projectId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT
                rev.RevenueID,
                p.ProjectID,
                rev.EffectiveDate,
                rev.Revenue,
                rev.ChangeReason,
                rev.RecordedBy,
                RecordedByName = LTRIM(RTRIM(CONCAT(u.FirstName, N' ', u.LastName))),
                rev.CreatedDate
              FROM dbo.tProjectRevenue AS rev
              INNER JOIN dbo.tProjectRef AS pr ON pr.ProjectRefID = rev.ProjectRefID
              INNER JOIN dbo.tProject AS p ON p.ProjectID = pr.ProjectID
              LEFT JOIN dbo.tUser AS u ON u.UserID = rev.RecordedBy
             WHERE p.ProjectID = @ProjectID
               AND rev.IsActive = 1
             ORDER BY rev.EffectiveDate DESC, rev.RevenueID DESC;
            """;

        return _db.QueryTextAsync(sql, MapRevenue,
        [
            SqlParameterFactory.Int("@ProjectID", projectId)
        ], cancellationToken);
    }

    public async Task<int> AddRevenueAsync(ProjectRevenue revenue, CancellationToken cancellationToken = default)
    {
        if (revenue.ProjectID <= 0)
        {
            throw new InvalidOperationException("ProjectID is required.");
        }

        var projectRefId = await ResolveProjectRefIdForProjectAsync(revenue.ProjectID, cancellationToken);
        return await InsertRevenueAsync(projectRefId, revenue, cancellationToken);
    }

    public async Task<int> DeleteRevenueAsync(int revenueId, CancellationToken cancellationToken = default)
    {
        await _db.ExecuteAsync("dbo.usp_ProjectRevenue_SoftDelete",
        [
            SqlParameterFactory.Int("@RevenueID", revenueId),
            SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId)
        ], cancellationToken);
        return revenueId;
    }

    public Task<IReadOnlyList<ProjectService>> ListServicesAsync(int projectId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT
                p.ProjectID,
                s.ServiceID,
                s.ServiceName,
                sc.CategoryName,
                ps.Frequency,
                ps.StartDate,
                ps.EndDate,
                ps.CreatedDate
              FROM dbo.tProjectService AS ps
              INNER JOIN dbo.tProjectRef AS pr ON pr.ProjectRefID = ps.ProjectRefID AND pr.IsActive = 1
              INNER JOIN dbo.tProject AS p ON p.ProjectID = pr.ProjectID
              INNER JOIN dbo.tServiceRef AS sr ON sr.ServiceRefID = ps.ServiceRefID AND sr.IsActive = 1
              INNER JOIN dbo.tService AS s ON s.ServiceID = sr.ServiceID AND s.IsActive = 1
              LEFT JOIN dbo.tServiceCategory AS sc ON sc.ServiceCategoryID = sr.ServiceCategoryID
             WHERE p.ProjectID = @ProjectID
               AND ps.IsActive = 1
             ORDER BY s.ServiceName;
            """;

        return _db.QueryTextAsync(sql, MapProjectService,
        [
            SqlParameterFactory.Int("@ProjectID", projectId)
        ], cancellationToken);
    }

    public Task<int> AddServiceAsync(ProjectService link, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Use SaveProjectAndReplaceServicesAsync to link services.");

    public Task<int> RemoveServiceAsync(int projectId, int serviceId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Use SaveProjectAndReplaceServicesAsync to replace services.");

    public async Task<int> SaveProjectAndReplaceServicesAsync(
        Project project,
        IReadOnlyList<int> serviceIds,
        ProjectRevenue? revenueEntry = null,
        ProjectClientRefContext? clientRefContext = null,
        CancellationToken cancellationToken = default)
    {
        if (project.ClientID <= 0)
        {
            throw new InvalidOperationException("ClientID is required.");
        }

        if (project.ProjectTypeID <= 0)
        {
            throw new InvalidOperationException("ProjectTypeID is required.");
        }

        await using var connection = await _connections.CreateOpenConnectionAsync(cancellationToken);
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            int projectId;
            if (project.ProjectID > 0)
            {
                projectId = project.ProjectID;
                await ExecuteProcAsync(connection, tx, "dbo.usp_Project_Update",
                [
                    SqlParameterFactory.Int("@ProjectID", projectId),
                    SqlParameterFactory.VarChar("@ProjectNumber", 50, project.ProjectNumber),
                    SqlParameterFactory.VarChar("@ProjectName", 200, project.ProjectName),
                    SqlParameterFactory.Date("@RenewalDate", project.RenewalDate),
                    SqlParameterFactory.Date("@DueDate", project.DueDate),
                    SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId)
                ], cancellationToken);
            }
            else
            {
                var output = SqlParameterFactory.OutputInt("@NewProjectID");
                await ExecuteProcAsync(connection, tx, "dbo.usp_Project_Create",
                [
                    SqlParameterFactory.VarChar("@ProjectNumber", 50, project.ProjectNumber),
                    SqlParameterFactory.VarChar("@ProjectName", 200, project.ProjectName),
                    SqlParameterFactory.Date("@RenewalDate", project.RenewalDate),
                    SqlParameterFactory.Date("@DueDate", project.DueDate),
                    SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId),
                    output
                ], cancellationToken);
                projectId = Convert.ToInt32(output.Value, CultureInfo.InvariantCulture);
            }

            var clientRefId = await ResolveClientRefIdAsync(connection, tx, project.ClientID, clientRefContext, cancellationToken);
            var statusId = project.StatusID > 0 ? project.StatusID : 1;
            var projectRefId = await ResolveProjectRefIdAsync(
                connection,
                tx,
                projectId,
                clientRefId,
                project.ProjectTypeID,
                statusId,
                project.UGID,
                cancellationToken);

            await SyncProjectActiveFromStatusAsync(connection, tx, projectId, statusId, cancellationToken);
            await SyncProjectOnlineAsync(connection, tx, projectId, project.IsOnline, cancellationToken);
            await SyncProjectFeeBasedRevenueAsync(connection, tx, projectId, project.IsFeeBasedRevenue, cancellationToken);

            await ExecuteTextAsync(connection, tx,
                "DELETE FROM dbo.tProjectService WHERE ProjectRefID = @ProjectRefID;",
                [SqlParameterFactory.Int("@ProjectRefID", projectRefId)],
                cancellationToken);

            foreach (var serviceId in serviceIds.Distinct())
            {
                if (serviceId <= 0)
                {
                    continue;
                }

                var serviceRefId = await QueryScalarIntAsync(connection, tx, """
                    SELECT TOP (1) ServiceRefID
                      FROM dbo.tServiceRef
                     WHERE ServiceID = @ServiceID AND IsActive = 1
                     ORDER BY ServiceRefID DESC;
                    """,
                [
                    SqlParameterFactory.Int("@ServiceID", serviceId)
                ], cancellationToken);

                if (serviceRefId is null or <= 0)
                {
                    continue;
                }

                var serviceOutput = SqlParameterFactory.OutputInt("@NewProjectServiceID");
                await ExecuteProcAsync(connection, tx, "dbo.usp_ProjectService_Create",
                [
                    SqlParameterFactory.Int("@ProjectRefID", projectRefId),
                    SqlParameterFactory.Int("@ServiceRefID", serviceRefId.Value),
                    SqlParameterFactory.VarChar("@Frequency", 20, null),
                    SqlParameterFactory.Date("@StartDate", null),
                    SqlParameterFactory.Date("@EndDate", null),
                    SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId),
                    serviceOutput
                ], cancellationToken);
            }

            await ExecuteTextAsync(connection, tx,
                "DELETE FROM dbo.tProjectUser WHERE ProjectRefID = @ProjectRefID;",
                [SqlParameterFactory.Int("@ProjectRefID", projectRefId)],
                cancellationToken);

            if (project.UGID > 0)
            {
                var memberRows = await QueryMemberRowsAsync(connection, tx, project.UGID, cancellationToken);
                foreach (var member in memberRows)
                {
                    var userOutput = SqlParameterFactory.OutputInt("@NewProjectUserID");
                    await ExecuteProcAsync(connection, tx, "dbo.usp_ProjectUser_Create",
                    [
                        SqlParameterFactory.Int("@ProjectRefID", projectRefId),
                        SqlParameterFactory.Int("@UserID", member.UserId),
                        SqlParameterFactory.Int("@UserRoleID", member.UserRoleId),
                        SqlParameterFactory.Date("@StartDate", null),
                        SqlParameterFactory.Date("@EndDate", null),
                        SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId),
                        userOutput
                    ], cancellationToken);
                }
            }

            if (revenueEntry is { Revenue: > 0 })
            {
                revenueEntry.ProjectID = projectId;
                await InsertRevenueInTransactionAsync(connection, tx, projectRefId, revenueEntry, cancellationToken);
            }

            await tx.CommitAsync(cancellationToken);
            return projectId;
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task EnsureProjectNoteSchemaAsync(CancellationToken cancellationToken = default)
    {
        // Each ALTER that references a newly added column must run in its own batch.
        const string addColumnsSql = """
            IF OBJECT_ID(N'dbo.tProjectNote', N'U') IS NULL
                RETURN;

            IF COL_LENGTH(N'dbo.tProjectNote', N'CreatedByUserID') IS NULL
            BEGIN
                ALTER TABLE dbo.tProjectNote ADD CreatedByUserID INT NULL;
            END;

            IF COL_LENGTH(N'dbo.tProjectNote', N'CalendarItemID') IS NULL
            BEGIN
                ALTER TABLE dbo.tProjectNote ADD CalendarItemID INT NULL;
            END;

            IF COL_LENGTH(N'dbo.tProjectNote', N'CalendarItemTitle') IS NULL
            BEGIN
                ALTER TABLE dbo.tProjectNote ADD CalendarItemTitle NVARCHAR(200) NULL;
            END;

            IF COL_LENGTH(N'dbo.tProjectNote', N'ProjectFileID') IS NULL
            BEGIN
                ALTER TABLE dbo.tProjectNote ADD ProjectFileID INT NULL;
            END;

            IF COL_LENGTH(N'dbo.tProjectNote', N'ProjectFileName') IS NULL
            BEGIN
                ALTER TABLE dbo.tProjectNote ADD ProjectFileName NVARCHAR(260) NULL;
            END;

            IF COL_LENGTH(N'dbo.tProjectNote', N'MentionedUserIDs') IS NULL
            BEGIN
                ALTER TABLE dbo.tProjectNote ADD MentionedUserIDs NVARCHAR(500) NULL;
            END;
            """;

        const string addCreatedByFkSql = """
            IF OBJECT_ID(N'dbo.tProjectNote', N'U') IS NULL
                RETURN;

            IF COL_LENGTH(N'dbo.tProjectNote', N'CreatedByUserID') IS NULL
                RETURN;

            IF OBJECT_ID(N'dbo.FK_tProjectNote_CreatedByUserID', N'F') IS NULL
               AND OBJECT_ID(N'dbo.tUser', N'U') IS NOT NULL
            BEGIN
                ALTER TABLE dbo.tProjectNote
                    ADD CONSTRAINT FK_tProjectNote_CreatedByUserID
                    FOREIGN KEY (CreatedByUserID) REFERENCES dbo.tUser (UserID);
            END;
            """;

        const string addCalendarFkSql = """
            IF OBJECT_ID(N'dbo.tProjectNote', N'U') IS NULL
                RETURN;

            IF COL_LENGTH(N'dbo.tProjectNote', N'CalendarItemID') IS NULL
                RETURN;

            IF OBJECT_ID(N'dbo.FK_tProjectNote_CalendarItemID', N'F') IS NULL
               AND OBJECT_ID(N'dbo.tProjectCalendarItem', N'U') IS NOT NULL
            BEGIN
                ALTER TABLE dbo.tProjectNote
                    ADD CONSTRAINT FK_tProjectNote_CalendarItemID
                    FOREIGN KEY (CalendarItemID) REFERENCES dbo.tProjectCalendarItem (CalendarItemID);
            END;
            """;

        const string addFileFkSql = """
            IF OBJECT_ID(N'dbo.tProjectNote', N'U') IS NULL
                RETURN;

            IF COL_LENGTH(N'dbo.tProjectNote', N'ProjectFileID') IS NULL
                RETURN;

            IF OBJECT_ID(N'dbo.FK_tProjectNote_ProjectFileID', N'F') IS NULL
               AND OBJECT_ID(N'dbo.tProjectFile', N'U') IS NOT NULL
            BEGIN
                ALTER TABLE dbo.tProjectNote
                    ADD CONSTRAINT FK_tProjectNote_ProjectFileID
                    FOREIGN KEY (ProjectFileID) REFERENCES dbo.tProjectFile (ProjectFileID)
                    ON DELETE SET NULL;
            END;
            """;

        const string addIndexSql = """
            SET QUOTED_IDENTIFIER ON;

            IF OBJECT_ID(N'dbo.tProjectNote', N'U') IS NULL
                RETURN;

            IF COL_LENGTH(N'dbo.tProjectNote', N'CalendarItemID') IS NULL
                RETURN;

            IF NOT EXISTS (
                SELECT 1
                  FROM sys.indexes
                 WHERE name = N'IX_tProjectNote_CalendarItemID'
                   AND object_id = OBJECT_ID(N'dbo.tProjectNote')
            )
            BEGIN
                CREATE NONCLUSTERED INDEX IX_tProjectNote_CalendarItemID
                    ON dbo.tProjectNote (CalendarItemID)
                    WHERE CalendarItemID IS NOT NULL;
            END;

            IF COL_LENGTH(N'dbo.tProjectNote', N'ProjectFileID') IS NOT NULL
               AND NOT EXISTS (
                SELECT 1
                  FROM sys.indexes
                 WHERE name = N'IX_tProjectNote_ProjectFileID'
                   AND object_id = OBJECT_ID(N'dbo.tProjectNote')
            )
            BEGIN
                CREATE NONCLUSTERED INDEX IX_tProjectNote_ProjectFileID
                    ON dbo.tProjectNote (ProjectFileID)
                    WHERE ProjectFileID IS NOT NULL;
            END;
            """;

        const string procSql = """
            CREATE OR ALTER PROCEDURE dbo.usp_ProjectNote_Create
                @ProjectRefID                 INT,
                @NoteDate                     DATETIME2(0),
                @NoteType                     VARCHAR(30),
                @NoteText                     NVARCHAR(MAX),
                @IsPinned                     BIT,
                @ActingUserID                 INT,
                @CreatedByUserID              INT = NULL,
                @CalendarItemID               INT = NULL,
                @CalendarItemTitle            NVARCHAR(200) = NULL,
                @ProjectFileID                INT = NULL,
                @ProjectFileName              NVARCHAR(260) = NULL,
                @MentionedUserIDs             NVARCHAR(500) = NULL,
                @NewNoteID                    INT OUTPUT
            AS
            BEGIN
                SET NOCOUNT ON;
                SET XACT_ABORT ON;

                BEGIN TRY
                    BEGIN TRAN;

                    INSERT INTO dbo.tProjectNote (
                        ProjectRefID,
                        NoteDate,
                        NoteType,
                        NoteText,
                        IsPinned,
                        CreatedByUserID,
                        CalendarItemID,
                        CalendarItemTitle,
                        ProjectFileID,
                        ProjectFileName,
                        MentionedUserIDs
                    )
                    VALUES (
                        @ProjectRefID,
                        @NoteDate,
                        @NoteType,
                        @NoteText,
                        @IsPinned,
                        @CreatedByUserID,
                        @CalendarItemID,
                        @CalendarItemTitle,
                        @ProjectFileID,
                        @ProjectFileName,
                        @MentionedUserIDs
                    );

                    SET @NewNoteID = SCOPE_IDENTITY();

                    EXEC dbo.usp_ActivityLog_Insert
                        @EntityType = 'ProjectNote',
                        @EntityID   = @NewNoteID,
                        @UserID     = @ActingUserID,
                        @Action     = 'Insert';

                    COMMIT TRAN;
                END TRY
                BEGIN CATCH
                    IF @@TRANCOUNT > 0 ROLLBACK TRAN;
                    THROW;
                END CATCH
            END;
            """;

        await _db.ExecuteTextAsync(addColumnsSql, cancellationToken: cancellationToken);
        await _db.ExecuteTextAsync(addCreatedByFkSql, cancellationToken: cancellationToken);
        await _db.ExecuteTextAsync(addCalendarFkSql, cancellationToken: cancellationToken);
        await _db.ExecuteTextAsync(addFileFkSql, cancellationToken: cancellationToken);
        await _db.ExecuteTextAsync(addIndexSql, cancellationToken: cancellationToken);
        await _db.ExecuteTextAsync(procSql, cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<ProjectNote>> ListNotesAsync(int projectId, CancellationToken cancellationToken = default)
    {
        await EnsureProjectNoteSchemaAsync(cancellationToken);

        const string sql = """
            SELECT
                pn.NoteID,
                p.ProjectID,
                NoteUserID = pn.CreatedByUserID,
                UserFullName = LTRIM(RTRIM(CONCAT(u.FirstName, N' ', u.LastName))),
                pn.NoteDate,
                pn.NoteType,
                pn.NoteText,
                pn.IsPinned,
                pn.CalendarItemID,
                pn.CalendarItemTitle,
                pn.ProjectFileID,
                ProjectFileName = COALESCE(
                    NULLIF(LTRIM(RTRIM(pn.ProjectFileName)), N''),
                    NULLIF(LTRIM(RTRIM(pf.OriginalFileName)), N'')),
                pn.MentionedUserIDs,
                pn.CreatedDate,
                pn.ModifiedDate
              FROM dbo.tProjectNote AS pn
              INNER JOIN dbo.tProjectRef AS pr ON pr.ProjectRefID = pn.ProjectRefID AND pr.IsActive = 1
              INNER JOIN dbo.tProject AS p ON p.ProjectID = pr.ProjectID
              LEFT JOIN dbo.tUser AS u ON u.UserID = pn.CreatedByUserID
              LEFT JOIN dbo.tProjectFile AS pf ON pf.ProjectFileID = pn.ProjectFileID
             WHERE p.ProjectID = @ProjectID
               AND pn.IsActive = 1
             ORDER BY pn.NoteDate DESC, pn.NoteID DESC;
            """;

        return await _db.QueryTextAsync(sql, MapNote,
        [
            SqlParameterFactory.Int("@ProjectID", projectId)
        ], cancellationToken);
    }

    public async Task<int> AddNoteAsync(ProjectNote note, CancellationToken cancellationToken = default)
    {
        if (note.ProjectID <= 0)
        {
            throw new InvalidOperationException("ProjectID is required.");
        }

        await EnsureProjectNoteSchemaAsync(cancellationToken);

        var projectRefId = await ResolveProjectRefIdForProjectAsync(note.ProjectID, cancellationToken);
        var noteDate = note.NoteDate == default ? DateTime.UtcNow : note.NoteDate;
        var actingUserId = note.UserID > 0 ? note.UserID : CmsDefaults.ActingUserId;
        var createdByUserId = note.UserID > 0 ? note.UserID : (int?)null;
        var calendarItemId = note.CalendarItemID is > 0 ? note.CalendarItemID : null;
        var calendarItemTitle = string.IsNullOrWhiteSpace(note.CalendarItemTitle)
            ? null
            : note.CalendarItemTitle.Trim();
        var projectFileId = note.ProjectFileID is > 0 ? note.ProjectFileID : null;
        var projectFileName = string.IsNullOrWhiteSpace(note.ProjectFileName)
            ? null
            : note.ProjectFileName.Trim();
        if (projectFileName is { Length: > 260 })
        {
            projectFileName = projectFileName[..260];
        }

        var mentioned = NormalizeMentionedUserIds(note);
        if (projectFileId is not null)
        {
            note.NoteType = FileNoteKinds.Normalize(note.NoteType);
        }

        var output = SqlParameterFactory.OutputInt("@NewNoteID");
        await _db.ExecuteWithOutputIntAsync(
            "dbo.usp_ProjectNote_Create",
            [
                SqlParameterFactory.Int("@ProjectRefID", projectRefId),
                SqlParameterFactory.DateTime2("@NoteDate", noteDate),
                SqlParameterFactory.VarChar("@NoteType", 30, note.NoteType),
                SqlParameterFactory.NVarChar("@NoteText", -1, note.NoteText),
                SqlParameterFactory.Bit("@IsPinned", note.IsPinned),
                SqlParameterFactory.Int("@ActingUserID", actingUserId),
                SqlParameterFactory.Int("@CreatedByUserID", createdByUserId),
                SqlParameterFactory.Int("@CalendarItemID", calendarItemId),
                SqlParameterFactory.NVarChar("@CalendarItemTitle", 200, calendarItemTitle),
                SqlParameterFactory.Int("@ProjectFileID", projectFileId),
                SqlParameterFactory.NVarChar("@ProjectFileName", 260, projectFileName),
                SqlParameterFactory.NVarChar("@MentionedUserIDs", 500, mentioned),
                output
            ],
            "@NewNoteID",
            cancellationToken);
        note.MentionedUserIDs = mentioned;

        return Convert.ToInt32(output.Value, CultureInfo.InvariantCulture);
    }

    public Task<int> UpdateNoteAsync(ProjectNote note, CancellationToken cancellationToken = default)
        => _db.ExecuteScalarIntAsync("dbo.usp_ProjectNote_Update",
        [
            SqlParameterFactory.Int("@NoteID", note.NoteID),
            SqlParameterFactory.VarChar("@NoteType", 30, note.NoteType),
            SqlParameterFactory.NVarChar("@NoteText", -1, note.NoteText),
            SqlParameterFactory.Bit("@IsPinned", note.IsPinned),
            SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId)
        ], cancellationToken);

    public async Task<int> DeleteNoteAsync(int noteId, CancellationToken cancellationToken = default)
    {
        await _db.ExecuteAsync("dbo.usp_ProjectNote_SoftDelete",
        [
            SqlParameterFactory.Int("@NoteID", noteId),
            SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId)
        ], cancellationToken);
        return noteId;
    }

    public async Task EnsureProjectFeeBasedRevenueSchemaAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            IF OBJECT_ID(N'dbo.tProject', N'U') IS NULL
                RETURN;

            IF COL_LENGTH(N'dbo.tProject', N'IsFeeBasedRevenue') IS NULL
            BEGIN
                ALTER TABLE dbo.tProject
                    ADD IsFeeBasedRevenue BIT NOT NULL
                        CONSTRAINT DF_tProject_IsFeeBasedRevenue DEFAULT (0);
            END;
            """;

        await _db.ExecuteTextAsync(sql, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Allows ClientDeliverable on tProjectFile.Category (CHECK + create/update procs).
    /// Older DBs silently remapped unknown categories to SourceData.
    /// </summary>
    public async Task EnsureProjectFileCategorySchemaAsync(CancellationToken cancellationToken = default)
    {
        const string constraintSql = """
            IF OBJECT_ID(N'dbo.tProjectFile', N'U') IS NULL
                RETURN;

            IF EXISTS (
                SELECT 1
                  FROM sys.check_constraints
                 WHERE name = N'CK_tProjectFile_Category'
                   AND parent_object_id = OBJECT_ID(N'dbo.tProjectFile')
                   AND definition LIKE N'%ClientDeliverable%')
                RETURN;

            IF EXISTS (
                SELECT 1
                  FROM sys.check_constraints
                 WHERE name = N'CK_tProjectFile_Category'
                   AND parent_object_id = OBJECT_ID(N'dbo.tProjectFile'))
            BEGIN
                ALTER TABLE dbo.tProjectFile DROP CONSTRAINT CK_tProjectFile_Category;
            END;

            ALTER TABLE dbo.tProjectFile WITH NOCHECK
                ADD CONSTRAINT CK_tProjectFile_Category
                    CHECK (Category IN (
                        'SourceData',
                        'ExposureData',
                        'LossData',
                        'ClaimsData',
                        'ClientDeliverable',
                        'CorrespondenceData',
                        'SubmissionData'));
            """;

        const string createProcSql = """
            CREATE OR ALTER PROCEDURE dbo.usp_ProjectFile_Create
                @ProjectRefID      INT,
                @OriginalFileName  NVARCHAR(260),
                @ContentType       NVARCHAR(150) = NULL,
                @ByteLength        BIGINT,
                @BlobPath          NVARCHAR(500),
                @BlobUrl           NVARCHAR(1000),
                @Category          VARCHAR(40) = 'SourceData',
                @Description       NVARCHAR(1000) = NULL,
                @VersionLabel      NVARCHAR(40) = NULL,
                @DataAsOf          DATE = NULL,
                @IsCurrent         BIT = 0,
                @UploadedByUserID  INT,
                @ActingUserID      INT,
                @NewProjectFileID  INT OUTPUT
            AS
            BEGIN
                SET NOCOUNT ON;
                SET XACT_ABORT ON;

                IF NULLIF(LTRIM(RTRIM(@OriginalFileName)), N'') IS NULL
                    THROW 50001, 'OriginalFileName is required.', 1;

                IF NULLIF(LTRIM(RTRIM(@BlobPath)), N'') IS NULL
                    THROW 50002, 'BlobPath is required.', 1;

                IF @Category NOT IN (
                        'SourceData',
                        'ExposureData',
                        'LossData',
                        'ClaimsData',
                        'ClientDeliverable',
                        'CorrespondenceData',
                        'SubmissionData')
                    SET @Category = 'SourceData';

                IF NOT EXISTS (SELECT 1 FROM dbo.tProjectRef WHERE ProjectRefID = @ProjectRefID AND IsActive = 1)
                    THROW 50003, 'ProjectRef not found or inactive.', 1;

                SET @Description = NULLIF(LTRIM(RTRIM(@Description)), N'');
                SET @VersionLabel = NULLIF(LTRIM(RTRIM(@VersionLabel)), N'');
                SET @IsCurrent = ISNULL(@IsCurrent, 0);

                BEGIN TRY
                    BEGIN TRAN;

                    INSERT INTO dbo.tProjectFile
                    (
                        ProjectRefID, OriginalFileName, ContentType, ByteLength,
                        BlobPath, BlobUrl, Category, Description, VersionLabel, DataAsOf, IsCurrent, UploadedByUserID
                    )
                    VALUES
                    (
                        @ProjectRefID,
                        LTRIM(RTRIM(@OriginalFileName)),
                        @ContentType,
                        @ByteLength,
                        LTRIM(RTRIM(@BlobPath)),
                        LTRIM(RTRIM(@BlobUrl)),
                        @Category,
                        @Description,
                        @VersionLabel,
                        @DataAsOf,
                        @IsCurrent,
                        @UploadedByUserID
                    );

                    SET @NewProjectFileID = SCOPE_IDENTITY();

                    EXEC dbo.usp_ActivityLog_Insert
                        @EntityType = 'ProjectFile',
                        @EntityID   = @NewProjectFileID,
                        @UserID     = @ActingUserID,
                        @Action     = 'Insert';

                    COMMIT TRAN;
                END TRY
                BEGIN CATCH
                    IF @@TRANCOUNT > 0 ROLLBACK TRAN;
                    THROW;
                END CATCH
            END
            """;

        const string updateProcSql = """
            CREATE OR ALTER PROCEDURE dbo.usp_ProjectFile_UpdateMetadata
                @ProjectFileID INT,
                @Category      VARCHAR(40) = NULL,
                @Description   NVARCHAR(1000) = NULL,
                @VersionLabel  NVARCHAR(40) = NULL,
                @DataAsOf      DATE = NULL,
                @ActingUserID  INT
            AS
            BEGIN
                SET NOCOUNT ON;
                SET XACT_ABORT ON;

                IF NOT EXISTS (SELECT 1 FROM dbo.tProjectFile WHERE ProjectFileID = @ProjectFileID AND IsActive = 1)
                    THROW 50001, 'Project file not found or inactive.', 1;

                IF @Category IS NOT NULL
                   AND @Category NOT IN (
                        'SourceData',
                        'ExposureData',
                        'LossData',
                        'ClaimsData',
                        'ClientDeliverable',
                        'CorrespondenceData',
                        'SubmissionData')
                    SET @Category = 'SourceData';

                SET @Description = NULLIF(LTRIM(RTRIM(@Description)), N'');
                SET @VersionLabel = NULLIF(LTRIM(RTRIM(@VersionLabel)), N'');

                BEGIN TRY
                    BEGIN TRAN;

                    UPDATE dbo.tProjectFile
                       SET Category = COALESCE(@Category, Category),
                           Description = @Description,
                           VersionLabel = @VersionLabel,
                           DataAsOf = @DataAsOf,
                           ModifiedDate = SYSUTCDATETIME()
                     WHERE ProjectFileID = @ProjectFileID
                       AND IsActive = 1;

                    EXEC dbo.usp_ActivityLog_Insert
                        @EntityType = 'ProjectFile',
                        @EntityID   = @ProjectFileID,
                        @UserID     = @ActingUserID,
                        @Action     = 'Update';

                    COMMIT TRAN;
                END TRY
                BEGIN CATCH
                    IF @@TRANCOUNT > 0 ROLLBACK TRAN;
                    THROW;
                END CATCH
            END
            """;

        await _db.ExecuteTextAsync(constraintSql, cancellationToken: cancellationToken);
        await _db.ExecuteTextAsync(createProcSql, cancellationToken: cancellationToken);
        await _db.ExecuteTextAsync(updateProcSql, cancellationToken: cancellationToken);
    }

    /// <summary>Adds file approval status columns and refreshes list/get procs to return them.</summary>
    public async Task EnsureProjectFileApprovalSchemaAsync(CancellationToken cancellationToken = default)
    {
        const string columnsSql = """
            IF OBJECT_ID(N'dbo.tProjectFile', N'U') IS NULL
                RETURN;

            IF COL_LENGTH(N'dbo.tProjectFile', N'ApprovalStatus') IS NULL
            BEGIN
                ALTER TABLE dbo.tProjectFile
                    ADD ApprovalStatus VARCHAR(40) NOT NULL
                        CONSTRAINT DF_tProjectFile_ApprovalStatus DEFAULT ('None');
            END;

            IF COL_LENGTH(N'dbo.tProjectFile', N'ApprovalUpdatedByUserID') IS NULL
            BEGIN
                ALTER TABLE dbo.tProjectFile ADD ApprovalUpdatedByUserID INT NULL;
            END;

            IF COL_LENGTH(N'dbo.tProjectFile', N'ApprovalUpdatedDate') IS NULL
            BEGIN
                ALTER TABLE dbo.tProjectFile ADD ApprovalUpdatedDate DATETIME2(0) NULL;
            END;
            """;

        const string listProcSql = """
            CREATE OR ALTER PROCEDURE dbo.usp_ProjectFile_ListByProject
                @ProjectID INT
            AS
            BEGIN
                SET NOCOUNT ON;

                SELECT
                    f.ProjectFileID,
                    p.ProjectID,
                    f.ProjectRefID,
                    f.OriginalFileName,
                    f.ContentType,
                    f.ByteLength,
                    f.BlobPath,
                    f.BlobUrl,
                    f.Category,
                    f.Description,
                    f.VersionLabel,
                    f.DataAsOf,
                    f.IsCurrent,
                    f.ApprovalStatus,
                    f.ApprovalUpdatedByUserID,
                    ApprovalUpdatedByName = CASE
                        WHEN au.UserID IS NULL THEN NULL
                        ELSE LTRIM(RTRIM(CONCAT(au.FirstName, N' ', au.LastName)))
                    END,
                    f.ApprovalUpdatedDate,
                    f.UploadedByUserID,
                    UploadedByName = CASE
                        WHEN u.UserID IS NULL THEN NULL
                        ELSE LTRIM(RTRIM(CONCAT(u.FirstName, N' ', u.LastName)))
                    END,
                    f.IsActive,
                    f.CreatedDate,
                    f.ModifiedDate
                  FROM dbo.tProjectFile AS f
                  INNER JOIN dbo.tProjectRef AS pr ON pr.ProjectRefID = f.ProjectRefID AND pr.IsActive = 1
                  INNER JOIN dbo.tProject AS p ON p.ProjectID = pr.ProjectID
                  LEFT JOIN dbo.tUser AS u ON u.UserID = f.UploadedByUserID
                  LEFT JOIN dbo.tUser AS au ON au.UserID = f.ApprovalUpdatedByUserID
                 WHERE p.ProjectID = @ProjectID
                   AND f.IsActive = 1
                 ORDER BY f.IsCurrent DESC, f.CreatedDate DESC, f.ProjectFileID DESC;
            END
            """;

        const string getProcSql = """
            CREATE OR ALTER PROCEDURE dbo.usp_ProjectFile_GetById
                @ProjectFileID INT
            AS
            BEGIN
                SET NOCOUNT ON;

                SELECT
                    f.ProjectFileID,
                    p.ProjectID,
                    f.ProjectRefID,
                    f.OriginalFileName,
                    f.ContentType,
                    f.ByteLength,
                    f.BlobPath,
                    f.BlobUrl,
                    f.Category,
                    f.Description,
                    f.VersionLabel,
                    f.DataAsOf,
                    f.IsCurrent,
                    f.ApprovalStatus,
                    f.ApprovalUpdatedByUserID,
                    ApprovalUpdatedByName = CASE
                        WHEN au.UserID IS NULL THEN NULL
                        ELSE LTRIM(RTRIM(CONCAT(au.FirstName, N' ', au.LastName)))
                    END,
                    f.ApprovalUpdatedDate,
                    f.UploadedByUserID,
                    UploadedByName = CASE
                        WHEN u.UserID IS NULL THEN NULL
                        ELSE LTRIM(RTRIM(CONCAT(u.FirstName, N' ', u.LastName)))
                    END,
                    f.IsActive,
                    f.CreatedDate,
                    f.ModifiedDate
                  FROM dbo.tProjectFile AS f
                  INNER JOIN dbo.tProjectRef AS pr ON pr.ProjectRefID = f.ProjectRefID
                  INNER JOIN dbo.tProject AS p ON p.ProjectID = pr.ProjectID
                  LEFT JOIN dbo.tUser AS u ON u.UserID = f.UploadedByUserID
                  LEFT JOIN dbo.tUser AS au ON au.UserID = f.ApprovalUpdatedByUserID
                 WHERE f.ProjectFileID = @ProjectFileID
                   AND f.IsActive = 1;
            END
            """;

        await _db.ExecuteTextAsync(columnsSql, cancellationToken: cancellationToken);
        await _db.ExecuteTextAsync(listProcSql, cancellationToken: cancellationToken);
        await _db.ExecuteTextAsync(getProcSql, cancellationToken: cancellationToken);
    }

    public async Task EnsureProjectCalendarSchemaAsync(CancellationToken cancellationToken = default)
    {
        // Keep ADD COLUMN in its own batch. SQL Server cannot compile a CHECK on a column
        // that is added in the same batch (Invalid column name 'PercentComplete').
        const string addColumnSql = """
            IF OBJECT_ID(N'dbo.tProjectCalendarItem', N'U') IS NULL
                RETURN;

            IF COL_LENGTH(N'dbo.tProjectCalendarItem', N'PercentComplete') IS NULL
            BEGIN
                ALTER TABLE dbo.tProjectCalendarItem
                    ADD PercentComplete TINYINT NOT NULL
                        CONSTRAINT DF_tProjectCalendarItem_PercentComplete DEFAULT (0);
            END;
            """;

        const string addCheckSql = """
            IF OBJECT_ID(N'dbo.tProjectCalendarItem', N'U') IS NULL
                RETURN;

            IF COL_LENGTH(N'dbo.tProjectCalendarItem', N'PercentComplete') IS NULL
                RETURN;

            IF OBJECT_ID(N'dbo.CK_tProjectCalendarItem_PercentComplete', N'C') IS NULL
            BEGIN
                ALTER TABLE dbo.tProjectCalendarItem
                    ADD CONSTRAINT CK_tProjectCalendarItem_PercentComplete
                    CHECK (PercentComplete BETWEEN 0 AND 100);
            END;
            """;

        const string listSql = """
            CREATE OR ALTER PROCEDURE dbo.usp_ProjectCalendarItem_ListByProject
                @ProjectID INT
            AS
            BEGIN
                SET NOCOUNT ON;

                SELECT
                    ci.CalendarItemID,
                    p.ProjectID,
                    ci.ProjectRefID,
                    ci.ItemType,
                    ci.Title,
                    ci.OwnerUserID,
                    OwnerFullName = CASE
                        WHEN u.UserID IS NULL THEN NULL
                        ELSE LTRIM(RTRIM(CONCAT(u.FirstName, N' ', u.LastName)))
                    END,
                    ci.ReceivedDate,
                    ci.CompletedDate,
                    ci.PercentComplete,
                    ci.Notes,
                    ci.SortOrder,
                    ci.IsActive,
                    ci.CreatedDate,
                    ci.ModifiedDate
                  FROM dbo.tProjectCalendarItem AS ci
                  INNER JOIN dbo.tProjectRef AS pr ON pr.ProjectRefID = ci.ProjectRefID AND pr.IsActive = 1
                  INNER JOIN dbo.tProject AS p ON p.ProjectID = pr.ProjectID
                  LEFT JOIN dbo.tUser AS u ON u.UserID = ci.OwnerUserID
                 WHERE p.ProjectID = @ProjectID
                   AND ci.IsActive = 1
                 ORDER BY ci.SortOrder, ci.CalendarItemID;
            END;
            """;

        const string createSql = """
            CREATE OR ALTER PROCEDURE dbo.usp_ProjectCalendarItem_Create
                @ProjectRefID     INT,
                @ItemType         VARCHAR(30),
                @Title            NVARCHAR(200),
                @OwnerUserID      INT = NULL,
                @ReceivedDate     DATE = NULL,
                @CompletedDate    DATE = NULL,
                @PercentComplete  TINYINT = 0,
                @Notes            NVARCHAR(MAX) = NULL,
                @SortOrder        INT = 0,
                @ActingUserID     INT,
                @NewCalendarItemID INT OUTPUT
            AS
            BEGIN
                SET NOCOUNT ON;
                SET XACT_ABORT ON;

                IF @ItemType NOT IN ('Deliverable', 'Meeting', 'Task', 'Other')
                    THROW 50001, 'ItemType must be Deliverable, Meeting, Task, or Other.', 1;

                IF NULLIF(LTRIM(RTRIM(@Title)), N'') IS NULL
                    THROW 50002, 'Title is required.', 1;

                IF @PercentComplete > 100
                    THROW 50004, 'PercentComplete must be between 0 and 100.', 1;

                IF NOT EXISTS (SELECT 1 FROM dbo.tProjectRef WHERE ProjectRefID = @ProjectRefID AND IsActive = 1)
                    THROW 50003, 'ProjectRef not found or inactive.', 1;

                BEGIN TRY
                    BEGIN TRAN;

                    INSERT INTO dbo.tProjectCalendarItem
                    (
                        ProjectRefID, ItemType, Title, OwnerUserID,
                        ReceivedDate, CompletedDate, PercentComplete, Notes, SortOrder
                    )
                    VALUES
                    (
                        @ProjectRefID,
                        @ItemType,
                        LTRIM(RTRIM(@Title)),
                        @OwnerUserID,
                        @ReceivedDate,
                        @CompletedDate,
                        @PercentComplete,
                        @Notes,
                        @SortOrder
                    );

                    SET @NewCalendarItemID = SCOPE_IDENTITY();

                    EXEC dbo.usp_ActivityLog_Insert
                        @EntityType = 'ProjectCalendarItem',
                        @EntityID   = @NewCalendarItemID,
                        @UserID     = @ActingUserID,
                        @Action     = 'Insert';

                    COMMIT TRAN;
                END TRY
                BEGIN CATCH
                    IF @@TRANCOUNT > 0 ROLLBACK TRAN;
                    THROW;
                END CATCH
            END;
            """;

        const string updateSql = """
            CREATE OR ALTER PROCEDURE dbo.usp_ProjectCalendarItem_Update
                @CalendarItemID   INT,
                @ItemType         VARCHAR(30),
                @Title            NVARCHAR(200),
                @OwnerUserID      INT = NULL,
                @ReceivedDate     DATE = NULL,
                @CompletedDate    DATE = NULL,
                @PercentComplete  TINYINT = 0,
                @Notes            NVARCHAR(MAX) = NULL,
                @SortOrder        INT = 0,
                @ActingUserID     INT
            AS
            BEGIN
                SET NOCOUNT ON;
                SET XACT_ABORT ON;

                IF NOT EXISTS (SELECT 1 FROM dbo.tProjectCalendarItem WHERE CalendarItemID = @CalendarItemID AND IsActive = 1)
                    THROW 50001, 'Calendar item not found or inactive.', 1;

                IF @ItemType NOT IN ('Deliverable', 'Meeting', 'Task', 'Other')
                    THROW 50002, 'ItemType must be Deliverable, Meeting, Task, or Other.', 1;

                IF NULLIF(LTRIM(RTRIM(@Title)), N'') IS NULL
                    THROW 50003, 'Title is required.', 1;

                IF @PercentComplete > 100
                    THROW 50004, 'PercentComplete must be between 0 and 100.', 1;

                BEGIN TRY
                    BEGIN TRAN;

                    UPDATE dbo.tProjectCalendarItem
                       SET ItemType = @ItemType,
                           Title = LTRIM(RTRIM(@Title)),
                           OwnerUserID = @OwnerUserID,
                           ReceivedDate = @ReceivedDate,
                           CompletedDate = @CompletedDate,
                           PercentComplete = @PercentComplete,
                           Notes = @Notes,
                           SortOrder = @SortOrder,
                           ModifiedDate = SYSUTCDATETIME()
                     WHERE CalendarItemID = @CalendarItemID;

                    EXEC dbo.usp_ActivityLog_Insert
                        @EntityType = 'ProjectCalendarItem',
                        @EntityID   = @CalendarItemID,
                        @UserID     = @ActingUserID,
                        @Action     = 'Update';

                    COMMIT TRAN;
                END TRY
                BEGIN CATCH
                    IF @@TRANCOUNT > 0 ROLLBACK TRAN;
                    THROW;
                END CATCH
            END;
            """;

        await _db.ExecuteTextAsync(addColumnSql, cancellationToken: cancellationToken);
        await _db.ExecuteTextAsync(addCheckSql, cancellationToken: cancellationToken);
        await _db.ExecuteTextAsync(listSql, cancellationToken: cancellationToken);
        await _db.ExecuteTextAsync(createSql, cancellationToken: cancellationToken);
        await _db.ExecuteTextAsync(updateSql, cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<ProjectCalendarItem>> ListCalendarItemsAsync(
        int projectId,
        CancellationToken cancellationToken = default)
    {
        await EnsureProjectCalendarSchemaAsync(cancellationToken);
        return await _db.QueryAsync(
            "dbo.usp_ProjectCalendarItem_ListByProject",
            MapCalendarItem,
            [
                SqlParameterFactory.Int("@ProjectID", projectId)
            ],
            cancellationToken);
    }

    public async Task ReplaceCalendarItemsAsync(
        int projectId,
        IReadOnlyList<ProjectCalendarItem> items,
        CancellationToken cancellationToken = default)
    {
        await EnsureProjectCalendarSchemaAsync(cancellationToken);
        var projectRefId = await ResolveProjectRefIdForProjectAsync(projectId, cancellationToken);
        var existing = await ListCalendarItemsAsync(projectId, cancellationToken);
        var existingIds = existing.Select(row => row.CalendarItemID).ToHashSet();
        var keptIds = new HashSet<int>();

        var sort = 0;
        foreach (var item in items)
        {
            var title = (item.Title ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            var itemType = NormalizeCalendarItemType(item.ItemType);
            // Prefer client-provided order, but never drop SortOrder 0 (first row).
            var sortOrder = item.SortOrder >= 0 ? item.SortOrder : sort;
            var ownerUserId = item.OwnerUserID is > 0 ? item.OwnerUserID : null;
            var receivedDate = NormalizeCalendarDate(item.ReceivedDate);
            var completedDate = NormalizeCalendarDate(item.CompletedDate);
            var percentComplete = Math.Clamp(item.PercentComplete, 0, 100);
            var notes = string.IsNullOrWhiteSpace(item.Notes) ? null : item.Notes.Trim();

            if (item.CalendarItemID > 0 && existingIds.Contains(item.CalendarItemID))
            {
                await _db.ExecuteAsync(
                    "dbo.usp_ProjectCalendarItem_Update",
                    [
                        SqlParameterFactory.Int("@CalendarItemID", item.CalendarItemID),
                        SqlParameterFactory.VarChar("@ItemType", 30, itemType),
                        SqlParameterFactory.NVarChar("@Title", 200, title),
                        SqlParameterFactory.Int("@OwnerUserID", ownerUserId),
                        SqlParameterFactory.Date("@ReceivedDate", receivedDate),
                        SqlParameterFactory.Date("@CompletedDate", completedDate),
                        SqlParameterFactory.Int("@PercentComplete", percentComplete),
                        SqlParameterFactory.NVarChar("@Notes", -1, notes),
                        SqlParameterFactory.Int("@SortOrder", sortOrder),
                        SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId)
                    ],
                    cancellationToken);
                keptIds.Add(item.CalendarItemID);
            }
            else
            {
                var output = SqlParameterFactory.OutputInt("@NewCalendarItemID");
                await _db.ExecuteWithOutputIntAsync(
                    "dbo.usp_ProjectCalendarItem_Create",
                    [
                        SqlParameterFactory.Int("@ProjectRefID", projectRefId),
                        SqlParameterFactory.VarChar("@ItemType", 30, itemType),
                        SqlParameterFactory.NVarChar("@Title", 200, title),
                        SqlParameterFactory.Int("@OwnerUserID", ownerUserId),
                        SqlParameterFactory.Date("@ReceivedDate", receivedDate),
                        SqlParameterFactory.Date("@CompletedDate", completedDate),
                        SqlParameterFactory.Int("@PercentComplete", percentComplete),
                        SqlParameterFactory.NVarChar("@Notes", -1, notes),
                        SqlParameterFactory.Int("@SortOrder", sortOrder),
                        SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId),
                        output
                    ],
                    "@NewCalendarItemID",
                    cancellationToken);
            }

            sort++;
        }

        foreach (var row in existing)
        {
            if (keptIds.Contains(row.CalendarItemID))
            {
                continue;
            }

            await _db.ExecuteAsync(
                "dbo.usp_ProjectCalendarItem_SoftDelete",
                [
                    SqlParameterFactory.Int("@CalendarItemID", row.CalendarItemID),
                    SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId)
                ],
                cancellationToken);
        }
    }

    public Task<IReadOnlyList<ProjectTimeEntry>> ListTimeEntriesForWeekAsync(
        int projectId,
        DateTime weekStartDate,
        int? userId = null,
        CancellationToken cancellationToken = default)
        => _db.QueryAsync(
            "dbo.usp_ProjectTimeEntry_ListByProjectWeek",
            MapTimeEntry,
            [
                SqlParameterFactory.Int("@ProjectID", projectId),
                SqlParameterFactory.Date("@WeekStartDate", weekStartDate.Date),
                SqlParameterFactory.Int("@UserID", userId)
            ],
            cancellationToken);

    public async Task<ProjectTimeSummary> GetTimeSummaryAsync(int projectId, CancellationToken cancellationToken = default)
    {
        const string byAnalystSql = """
            SELECT
                te.UserID AS Id,
                ISNULL(NULLIF(LTRIM(RTRIM(CONCAT(u.FirstName, N' ', u.LastName))), N''), N'(Unknown analyst)') AS Label,
                CAST(NULL AS nvarchar(100)) AS Detail,
                CAST(SUM(te.Hours) AS decimal(18,2)) AS Hours
              FROM dbo.tProjectTimeEntry AS te
             INNER JOIN dbo.tProjectRef AS pr
                ON pr.ProjectRefID = te.ProjectRefID
               AND pr.IsActive = 1
              LEFT JOIN dbo.tUser AS u ON u.UserID = te.UserID
             WHERE pr.ProjectID = @ProjectID
               AND te.IsActive = 1
               AND te.Hours > 0
             GROUP BY te.UserID, u.FirstName, u.LastName
             ORDER BY Hours DESC, Label, te.UserID;
            """;

        const string byTaskSql = """
            SELECT
                te.CalendarItemID AS Id,
                ISNULL(NULLIF(LTRIM(RTRIM(ci.Title)), N''), N'(Untitled task)') AS Label,
                ISNULL(NULLIF(LTRIM(RTRIM(ci.ItemType)), N''), N'') AS Detail,
                CAST(SUM(te.Hours) AS decimal(18,2)) AS Hours
              FROM dbo.tProjectTimeEntry AS te
             INNER JOIN dbo.tProjectRef AS pr
                ON pr.ProjectRefID = te.ProjectRefID
               AND pr.IsActive = 1
              LEFT JOIN dbo.tProjectCalendarItem AS ci ON ci.CalendarItemID = te.CalendarItemID
             WHERE pr.ProjectID = @ProjectID
               AND te.IsActive = 1
               AND te.Hours > 0
             GROUP BY te.CalendarItemID, ci.Title, ci.ItemType
             ORDER BY Hours DESC, Label, te.CalendarItemID;
            """;

        static ProjectTimeSummaryRow MapRow(SqlDataReader r) => new()
        {
            Id = r.GetInt32OrDefault("Id"),
            Label = r.TryGetNullableString("Label") ?? string.Empty,
            Detail = r.TryGetNullableString("Detail"),
            Hours = r.GetNullableDecimal("Hours") ?? 0m
        };

        // SqlParameter instances cannot be reused across command collections.
        var byAnalyst = await _db.QueryTextAsync(
            byAnalystSql,
            MapRow,
            [SqlParameterFactory.Int("@ProjectID", projectId)],
            cancellationToken);
        var byTask = await _db.QueryTextAsync(
            byTaskSql,
            MapRow,
            [SqlParameterFactory.Int("@ProjectID", projectId)],
            cancellationToken);

        return new ProjectTimeSummary
        {
            ByAnalyst = byAnalyst,
            ByTask = byTask,
            TotalHours = byAnalyst.Sum(r => r.Hours)
        };
    }

    public async Task SaveTimeEntriesForWeekAsync(
        int projectId,
        DateTime weekStartDate,
        IReadOnlyList<ProjectTimeEntry> entries,
        int targetUserId,
        CancellationToken cancellationToken = default)
    {
        if (targetUserId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetUserId));
        }

        var projectRefId = await ResolveProjectRefIdForProjectAsync(projectId, cancellationToken);
        var monday = weekStartDate.Date;
        var sunday = monday.AddDays(6);

        // Clear cells not present / zero by upserting every submitted cell (0 hours soft-deletes).
        // Also clear existing week cells that were omitted from the payload.
        var existing = await ListTimeEntriesForWeekAsync(projectId, monday, targetUserId, cancellationToken);
        var submittedKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            if (entry.CalendarItemID <= 0)
            {
                continue;
            }

            var workDate = entry.WorkDate.Date;
            if (workDate < monday || workDate > sunday)
            {
                continue;
            }

            var hours = entry.Hours < 0m ? 0m : (entry.Hours > 24m ? 24m : entry.Hours);
            var mode = string.Equals(entry.EntryMode, "Allocated", StringComparison.OrdinalIgnoreCase)
                ? "Allocated"
                : "Exact";

            submittedKeys.Add($"{entry.CalendarItemID}|{workDate:yyyy-MM-dd}");

            var output = SqlParameterFactory.OutputInt("@NewTimeEntryID");
            await _db.ExecuteWithOutputIntAsync(
                "dbo.usp_ProjectTimeEntry_Upsert",
                [
                    SqlParameterFactory.Int("@ProjectRefID", projectRefId),
                    SqlParameterFactory.Int("@CalendarItemID", entry.CalendarItemID),
                    SqlParameterFactory.Date("@WorkDate", workDate),
                    SqlParameterFactory.Decimal("@Hours", hours),
                    SqlParameterFactory.VarChar("@EntryMode", 20, mode),
                    SqlParameterFactory.NVarChar("@Notes", 500, entry.Notes),
                    SqlParameterFactory.Int("@ActingUserID", targetUserId),
                    output
                ],
                "@NewTimeEntryID",
                cancellationToken);
        }

        foreach (var row in existing)
        {
            var key = $"{row.CalendarItemID}|{row.WorkDate:yyyy-MM-dd}";
            if (submittedKeys.Contains(key))
            {
                continue;
            }

            var output = SqlParameterFactory.OutputInt("@NewTimeEntryID");
            await _db.ExecuteWithOutputIntAsync(
                "dbo.usp_ProjectTimeEntry_Upsert",
                [
                    SqlParameterFactory.Int("@ProjectRefID", projectRefId),
                    SqlParameterFactory.Int("@CalendarItemID", row.CalendarItemID),
                    SqlParameterFactory.Date("@WorkDate", row.WorkDate.Date),
                    SqlParameterFactory.Decimal("@Hours", 0m),
                    SqlParameterFactory.VarChar("@EntryMode", 20, "Exact"),
                    SqlParameterFactory.NVarChar("@Notes", 500, null),
                    SqlParameterFactory.Int("@ActingUserID", targetUserId),
                    output
                ],
                "@NewTimeEntryID",
                cancellationToken);
        }
    }

    public Task<IReadOnlyList<ProjectTimeCatUser>> ListCatDepartmentUsersForProjectAsync(
        int projectId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT DISTINCT
                u.UserID,
                LTRIM(RTRIM(CONCAT(u.FirstName, N' ', u.LastName))) AS UserFullName,
                u.Email
              FROM dbo.tProject AS p
              INNER JOIN dbo.tProjectRef AS pr ON pr.ProjectID = p.ProjectID AND pr.IsActive = 1
              INNER JOIN dbo.tUserGroupRef AS ugr ON ugr.UserGroupID = pr.UserGroupID
              INNER JOIN dbo.tUser AS u ON u.UserID = ugr.UserID AND u.IsActive = 1
             WHERE p.ProjectID = @ProjectID
               AND UPPER(LTRIM(RTRIM(u.Department))) = 'CAT'
             ORDER BY UserFullName, u.UserID;
            """;

        return _db.QueryTextAsync(
            sql,
            static r => new ProjectTimeCatUser
            {
                UserID = r.GetInt32OrDefault("UserID"),
                UserFullName = r.TryGetNullableString("UserFullName") ?? string.Empty,
                Email = r.TryGetNullableString("Email"),
            },
            [SqlParameterFactory.Int("@ProjectID", projectId)],
            cancellationToken);
    }

    public async Task<IReadOnlyList<ProjectFile>> ListFilesAsync(
        int projectId,
        CancellationToken cancellationToken = default)
    {
        await EnsureProjectFileApprovalSchemaAsync(cancellationToken);
        return await _db.QueryAsync(
            "dbo.usp_ProjectFile_ListByProject",
            MapProjectFile,
            [SqlParameterFactory.Int("@ProjectID", projectId)],
            cancellationToken);
    }

    public async Task<ProjectFile?> GetFileAsync(
        int projectFileId,
        CancellationToken cancellationToken = default)
    {
        await EnsureProjectFileApprovalSchemaAsync(cancellationToken);
        var rows = await _db.QueryAsync(
            "dbo.usp_ProjectFile_GetById",
            MapProjectFile,
            [SqlParameterFactory.Int("@ProjectFileID", projectFileId)],
            cancellationToken);
        return rows.Count == 0 ? null : rows[0];
    }

    public async Task UpdateFileApprovalStatusAsync(
        int projectFileId,
        string approvalStatus,
        int actingUserId,
        CancellationToken cancellationToken = default)
    {
        await EnsureProjectFileApprovalSchemaAsync(cancellationToken);
        var status = FileApprovalStatuses.Normalize(approvalStatus);
        const string sql = """
            UPDATE dbo.tProjectFile
               SET ApprovalStatus = @ApprovalStatus,
                   ApprovalUpdatedByUserID = @ActingUserID,
                   ApprovalUpdatedDate = SYSUTCDATETIME(),
                   ModifiedDate = SYSUTCDATETIME()
             WHERE ProjectFileID = @ProjectFileID
               AND IsActive = 1;
            """;
        await _db.ExecuteTextAsync(
            sql,
            [
                SqlParameterFactory.Int("@ProjectFileID", projectFileId),
                SqlParameterFactory.VarChar("@ApprovalStatus", 40, status),
                SqlParameterFactory.Int("@ActingUserID", actingUserId > 0 ? actingUserId : CmsDefaults.ActingUserId)
            ],
            cancellationToken);
    }

    public async Task<int> AddFileAsync(ProjectFile file, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        await EnsureProjectFileCategorySchemaAsync(cancellationToken);

        var projectRefId = file.ProjectRefID > 0
            ? file.ProjectRefID
            : await ResolveProjectRefIdForProjectAsync(file.ProjectID, cancellationToken);

        var category = NormalizeFileCategory(file.Category);
        var uploadedBy = file.UploadedByUserID > 0 ? file.UploadedByUserID : CmsDefaults.ActingUserId;
        var output = SqlParameterFactory.OutputInt("@NewProjectFileID");

        await _db.ExecuteWithOutputIntAsync(
            "dbo.usp_ProjectFile_Create",
            [
                SqlParameterFactory.Int("@ProjectRefID", projectRefId),
                SqlParameterFactory.NVarChar("@OriginalFileName", 260, file.OriginalFileName),
                SqlParameterFactory.NVarChar("@ContentType", 150, file.ContentType),
                SqlParameterFactory.BigInt("@ByteLength", file.ByteLength),
                SqlParameterFactory.NVarChar("@BlobPath", 500, file.BlobPath),
                SqlParameterFactory.NVarChar("@BlobUrl", 1000, file.BlobUrl),
                SqlParameterFactory.VarChar("@Category", 40, category),
                SqlParameterFactory.NVarChar("@Description", 1000, file.Description),
                SqlParameterFactory.NVarChar("@VersionLabel", 40, file.VersionLabel),
                SqlParameterFactory.Date("@DataAsOf", file.DataAsOf),
                SqlParameterFactory.Bit("@IsCurrent", file.IsCurrent),
                SqlParameterFactory.Int("@UploadedByUserID", uploadedBy),
                SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId),
                output
            ],
            "@NewProjectFileID",
            cancellationToken);

        return Convert.ToInt32(output.Value, CultureInfo.InvariantCulture);
    }

    public async Task UpdateFileMetadataAsync(
        int projectFileId,
        string? category,
        string? description,
        string? versionLabel,
        DateTime? dataAsOf,
        CancellationToken cancellationToken = default)
    {
        await EnsureProjectFileCategorySchemaAsync(cancellationToken);
        var normalizedCategory = category is null ? null : NormalizeFileCategory(category);
        await _db.ExecuteAsync(
            "dbo.usp_ProjectFile_UpdateMetadata",
            [
                SqlParameterFactory.Int("@ProjectFileID", projectFileId),
                SqlParameterFactory.VarChar("@Category", 40, normalizedCategory),
                SqlParameterFactory.NVarChar("@Description", 1000, description),
                SqlParameterFactory.NVarChar("@VersionLabel", 40, versionLabel),
                SqlParameterFactory.Date("@DataAsOf", dataAsOf),
                SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId)
            ],
            cancellationToken);
    }

    public Task SetFileCurrentAsync(
        int projectFileId,
        bool isCurrent,
        CancellationToken cancellationToken = default)
        => _db.ExecuteAsync(
            "dbo.usp_ProjectFile_SetCurrent",
            [
                SqlParameterFactory.Int("@ProjectFileID", projectFileId),
                SqlParameterFactory.Bit("@IsCurrent", isCurrent),
                SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId)
            ],
            cancellationToken);

    public async Task SoftDeleteFileAsync(int projectFileId, CancellationToken cancellationToken = default)
    {
        await _db.ExecuteAsync(
            "dbo.usp_ProjectFile_SoftDelete",
            [
                SqlParameterFactory.Int("@ProjectFileID", projectFileId),
                SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId)
            ],
            cancellationToken);
    }

    public Task UpdateFileBlobLocationAsync(
        int projectFileId,
        string blobPath,
        string blobUrl,
        CancellationToken cancellationToken = default)
    {
        if (projectFileId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(projectFileId));
        }

        if (string.IsNullOrWhiteSpace(blobPath))
        {
            throw new ArgumentException("Blob path is required.", nameof(blobPath));
        }

        if (string.IsNullOrWhiteSpace(blobUrl))
        {
            throw new ArgumentException("Blob URL is required.", nameof(blobUrl));
        }

        const string sql = """
            UPDATE dbo.tProjectFile
               SET BlobPath = @BlobPath,
                   BlobUrl = @BlobUrl,
                   ModifiedDate = SYSUTCDATETIME()
             WHERE ProjectFileID = @ProjectFileID
               AND IsActive = 1;
            """;

        return _db.ExecuteTextAsync(
            sql,
            [
                SqlParameterFactory.Int("@ProjectFileID", projectFileId),
                SqlParameterFactory.NVarChar("@BlobPath", 500, blobPath.Trim()),
                SqlParameterFactory.NVarChar("@BlobUrl", 1000, blobUrl.Trim())
            ],
            cancellationToken);
    }

    private static string NormalizeFileCategory(string? category)
    {
        var value = (category ?? string.Empty).Trim();
        return value switch
        {
            "SourceData" or "ExposureData" or "LossData" or "ClaimsData" or "ClientDeliverable" or "CorrespondenceData" or "SubmissionData" => value,
            "Source Data" => "SourceData",
            "Client Deliverable" => "ClientDeliverable",
            "Exposure Data" => "ExposureData",
            "Loss Data" => "LossData",
            "Claims Data" => "ClaimsData",
            "Correspondence" => "CorrespondenceData",
            "Submission" => "SubmissionData",
            // Legacy values mapped forward
            "ClientData" or "Contract" or "Invoice" or "Other" => "SourceData",
            _ => "SourceData"
        };
    }

    private static DateTime? NormalizeCalendarDate(DateTime? value)
        => value.HasValue ? DateTime.SpecifyKind(value.Value.Date, DateTimeKind.Unspecified) : null;

    private static string NormalizeCalendarItemType(string? itemType)
    {
        var value = (itemType ?? string.Empty).Trim();
        return value switch
        {
            "Deliverable" or "Meeting" or "Task" or "Other" => value,
            _ => "Task"
        };
    }

    private async Task<Project?> QueryProjectAsync(int projectId, CancellationToken cancellationToken)
    {
        var sql = ProjectSelectSql + " WHERE p.ProjectID = @ProjectID;";
        var rows = await _db.QueryTextAsync(sql, MapProject,
        [
            SqlParameterFactory.Int("@ProjectID", projectId)
        ], cancellationToken);
        return rows.Count == 0 ? null : rows[0];
    }

    private static async Task<int> ResolveClientRefIdAsync(
        SqlConnection connection,
        SqlTransaction tx,
        int clientId,
        ProjectClientRefContext? clientRefContext,
        CancellationToken cancellationToken)
    {
        var businessUnitId = clientRefContext?.BusinessUnitId is > 0 ? clientRefContext.BusinessUnitId : null;
        var businessClassId = clientRefContext?.BusinessClassId is > 0 ? clientRefContext.BusinessClassId : null;
        var clientTypeId = clientRefContext?.ClientTypeId is > 0 ? clientRefContext.ClientTypeId : null;
        var statusId = clientRefContext?.ClientStatusId is > 0 ? clientRefContext.ClientStatusId!.Value : 1;

        if (businessUnitId is > 0)
        {
            var matchingRefId = await QueryScalarIntAsync(connection, tx, """
                SELECT TOP (1) ClientRefID
                  FROM dbo.tClientRef
                 WHERE ClientID = @ClientID
                   AND IsActive = 1
                   AND BusinessUnitID = @BusinessUnitID
                 ORDER BY ClientRefID;
                """,
            [
                SqlParameterFactory.Int("@ClientID", clientId),
                SqlParameterFactory.Int("@BusinessUnitID", businessUnitId.Value)
            ], cancellationToken);

            if (matchingRefId is > 0)
            {
                if (businessClassId is not null || clientTypeId is not null || clientRefContext?.ClientStatusId is > 0)
                {
                    await ExecuteProcAsync(connection, tx, "dbo.usp_ClientRef_Update",
                    [
                        SqlParameterFactory.Int("@ClientRefID", matchingRefId.Value),
                        SqlParameterFactory.Int("@ClientID", clientId),
                        SqlParameterFactory.Int("@BusinessUnitID", businessUnitId),
                        SqlParameterFactory.Int("@BusinessClassID", businessClassId),
                        SqlParameterFactory.Int("@ClientTypeID", clientTypeId),
                        SqlParameterFactory.Int("@ClientTypeRefID", null),
                        SqlParameterFactory.Int("@StatusID", statusId),
                        SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId)
                    ], cancellationToken);
                }

                return matchingRefId.Value;
            }

            var createOutput = SqlParameterFactory.OutputInt("@NewClientRefID");
            await ExecuteProcAsync(connection, tx, "dbo.usp_ClientRef_Create",
            [
                SqlParameterFactory.Int("@ClientID", clientId),
                SqlParameterFactory.Int("@BusinessUnitID", businessUnitId),
                SqlParameterFactory.Int("@BusinessClassID", businessClassId),
                SqlParameterFactory.Int("@ClientTypeID", clientTypeId),
                SqlParameterFactory.Int("@ClientTypeRefID", null),
                SqlParameterFactory.Int("@StatusID", statusId),
                SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId),
                createOutput
            ], cancellationToken);

            return Convert.ToInt32(createOutput.Value, CultureInfo.InvariantCulture);
        }

        var existing = await QueryScalarIntAsync(connection, tx, """
            SELECT TOP (1) ClientRefID
              FROM dbo.tClientRef
             WHERE ClientID = @ClientID AND IsActive = 1
             ORDER BY ClientRefID;
            """,
        [
            SqlParameterFactory.Int("@ClientID", clientId)
        ], cancellationToken);

        if (existing is > 0)
        {
            return existing.Value;
        }

        var output = SqlParameterFactory.OutputInt("@NewClientRefID");
        await ExecuteProcAsync(connection, tx, "dbo.usp_ClientRef_Create",
        [
            SqlParameterFactory.Int("@ClientID", clientId),
            SqlParameterFactory.Int("@BusinessUnitID", null),
            SqlParameterFactory.Int("@BusinessClassID", null),
            SqlParameterFactory.Int("@ClientTypeID", null),
            SqlParameterFactory.Int("@ClientTypeRefID", null),
            SqlParameterFactory.Int("@StatusID", statusId),
            SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId),
            output
        ], cancellationToken);

        return Convert.ToInt32(output.Value, CultureInfo.InvariantCulture);
    }

    private static async Task<int> ResolveProjectRefIdAsync(
        SqlConnection connection,
        SqlTransaction tx,
        int projectId,
        int clientRefId,
        int projectTypeId,
        int statusId,
        int userGroupId,
        CancellationToken cancellationToken)
    {
        var existing = await QueryScalarIntAsync(connection, tx, """
            SELECT TOP (1) ProjectRefID
              FROM dbo.tProjectRef
             WHERE ProjectID = @ProjectID AND IsActive = 1
             ORDER BY ProjectRefID;
            """,
        [
            SqlParameterFactory.Int("@ProjectID", projectId)
        ], cancellationToken);

        if (existing is > 0)
        {
            await ExecuteProcAsync(connection, tx, "dbo.usp_ProjectRef_Update",
            [
                SqlParameterFactory.Int("@ProjectRefID", existing.Value),
                SqlParameterFactory.Int("@ProjectID", projectId),
                SqlParameterFactory.Int("@ClientRefID", clientRefId),
                SqlParameterFactory.Int("@ProjectTypeID", projectTypeId),
                SqlParameterFactory.Int("@StatusID", statusId),
                // NULL means "leave existing UserGroupID alone". Passing 0 used to CLEAR the
                // column (USP maps <=0 to NULL), which made the form fall back to an inferred
                // group from tProjectUser and look like the save "reverted".
                SqlParameterFactory.Int("@UserGroupID", userGroupId > 0 ? userGroupId : null),
                SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId)
            ], cancellationToken);
            return existing.Value;
        }

        var output = SqlParameterFactory.OutputInt("@NewProjectRefID");
        await ExecuteProcAsync(connection, tx, "dbo.usp_ProjectRef_Create",
        [
            SqlParameterFactory.Int("@ProjectID", projectId),
            SqlParameterFactory.Int("@ClientRefID", clientRefId),
            SqlParameterFactory.Int("@ProjectTypeID", projectTypeId),
            SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId),
            SqlParameterFactory.Int("@StatusID", statusId),
            SqlParameterFactory.Int("@UserGroupID", userGroupId > 0 ? userGroupId : null),
            output
        ], cancellationToken);

        return Convert.ToInt32(output.Value, CultureInfo.InvariantCulture);
    }

    private static async Task SyncProjectOnlineAsync(
        SqlConnection connection,
        SqlTransaction tx,
        int projectId,
        bool isOnline,
        CancellationToken cancellationToken)
    {
        await ExecuteTextAsync(connection, tx,
            """
            UPDATE dbo.tProject
               SET IsOnline = @IsOnline,
                   ModifiedDate = SYSUTCDATETIME()
             WHERE ProjectID = @ProjectID;
            """,
        [
            SqlParameterFactory.Int("@ProjectID", projectId),
            SqlParameterFactory.Bit("@IsOnline", isOnline)
        ], cancellationToken);
    }

    private static async Task SyncProjectFeeBasedRevenueAsync(
        SqlConnection connection,
        SqlTransaction tx,
        int projectId,
        bool isFeeBasedRevenue,
        CancellationToken cancellationToken)
    {
        await ExecuteTextAsync(connection, tx,
            """
            UPDATE dbo.tProject
               SET IsFeeBasedRevenue = @IsFeeBasedRevenue,
                   ModifiedDate = SYSUTCDATETIME()
             WHERE ProjectID = @ProjectID;
            """,
        [
            SqlParameterFactory.Int("@ProjectID", projectId),
            SqlParameterFactory.Bit("@IsFeeBasedRevenue", isFeeBasedRevenue)
        ], cancellationToken);
    }

    private static async Task SyncProjectActiveFromStatusAsync(
        SqlConnection connection,
        SqlTransaction tx,
        int projectId,
        int statusId,
        CancellationToken cancellationToken)
    {
        // Seed data: StatusID 2 = Inactive ('I'). Keep dbo.tProject.IsActive aligned with status.
        var isActive = statusId != 2;
        await ExecuteTextAsync(connection, tx,
            """
            UPDATE dbo.tProject
               SET IsActive = @IsActive,
                   ModifiedDate = SYSUTCDATETIME()
             WHERE ProjectID = @ProjectID;
            """,
        [
            SqlParameterFactory.Int("@ProjectID", projectId),
            SqlParameterFactory.Bit("@IsActive", isActive)
        ], cancellationToken);
    }

    private static async Task<IReadOnlyList<(int UserId, int? UserRoleId)>> QueryMemberRowsAsync(
        SqlConnection connection,
        SqlTransaction tx,
        int userGroupId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT UserID, UserRoleID
              FROM dbo.tUserGroupRef
             WHERE UserGroupID = @UserGroupID;
            """;

        await using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.Parameters.Add(SqlParameterFactory.Int("@UserGroupID", userGroupId));

        var rows = new List<(int UserId, int? UserRoleId)>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add((reader.GetInt32(0), reader.IsDBNull(1) ? null : reader.GetInt32(1)));
        }

        return rows;
    }

    private static async Task<int?> QueryScalarIntAsync(
        SqlConnection connection,
        SqlTransaction tx,
        string sql,
        IEnumerable<SqlParameter> parameters,
        CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var parameter in parameters)
        {
            cmd.Parameters.Add(parameter);
        }

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result is null or DBNull ? null : Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    private static async Task ExecuteTextAsync(
        SqlConnection connection,
        SqlTransaction tx,
        string sql,
        IEnumerable<SqlParameter> parameters,
        CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var parameter in parameters)
        {
            cmd.Parameters.Add(parameter);
        }

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ExecuteProcAsync(
        SqlConnection connection,
        SqlTransaction tx,
        string procedureName,
        IEnumerable<SqlParameter> parameters,
        CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandType = System.Data.CommandType.StoredProcedure;
        cmd.CommandText = procedureName;
        foreach (var parameter in parameters)
        {
            cmd.Parameters.Add(parameter);
        }

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<int> ResolveProjectRefIdForProjectAsync(int projectId, CancellationToken cancellationToken)
    {
        const string refSql = """
            SELECT TOP (1) pr.ProjectRefID
              FROM dbo.tProjectRef AS pr
             WHERE pr.ProjectID = @ProjectID
               AND pr.IsActive = 1
             ORDER BY pr.ProjectRefID;
            """;

        var refRows = await _db.QueryTextAsync(
            refSql,
            static r => r.GetInt32OrDefault("ProjectRefID"),
            [SqlParameterFactory.Int("@ProjectID", projectId)],
            cancellationToken);

        if (refRows.Count == 0)
        {
            throw new InvalidOperationException($"No active ProjectRefID for ProjectID {projectId}.");
        }

        return refRows[0];
    }

    private Task<int> InsertRevenueAsync(int projectRefId, ProjectRevenue revenue, CancellationToken cancellationToken)
    {
        var output = SqlParameterFactory.OutputInt("@NewRevenueID");
        var parameters = new List<SqlParameter>
        {
            SqlParameterFactory.Int("@ProjectRefID", projectRefId),
            SqlParameterFactory.Date("@EffectiveDate", revenue.EffectiveDate),
            SqlParameterFactory.Decimal("@Revenue", revenue.Revenue),
            SqlParameterFactory.VarChar("@ChangeReason", 255, revenue.ChangeReason),
            SqlParameterFactory.Int(
                "@RecordedBy",
                revenue.RecordedBy > 0 ? revenue.RecordedBy : CmsDefaults.ActingUserId),
            SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId),
            output
        };

        return _db.ExecuteWithOutputIntAsync(
            "dbo.usp_ProjectRevenue_Create",
            parameters,
            "@NewRevenueID",
            cancellationToken);
    }

    private static async Task InsertRevenueInTransactionAsync(
        SqlConnection connection,
        SqlTransaction tx,
        int projectRefId,
        ProjectRevenue revenue,
        CancellationToken cancellationToken)
    {
        var output = SqlParameterFactory.OutputInt("@NewRevenueID");
        await ExecuteProcAsync(connection, tx, "dbo.usp_ProjectRevenue_Create",
        [
            SqlParameterFactory.Int("@ProjectRefID", projectRefId),
            SqlParameterFactory.Date("@EffectiveDate", revenue.EffectiveDate),
            SqlParameterFactory.Decimal("@Revenue", revenue.Revenue),
            SqlParameterFactory.VarChar("@ChangeReason", 255, revenue.ChangeReason),
            SqlParameterFactory.Int(
                "@RecordedBy",
                revenue.RecordedBy > 0 ? revenue.RecordedBy : CmsDefaults.ActingUserId),
            SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId),
            output
        ], cancellationToken);
    }

    private static Project MapProjectHeader(SqlDataReader r) => new()
    {
        ProjectID = r.GetInt32OrDefault("ProjectID"),
        ProjectNumber = r.TryGetNullableString("ProjectNumber") ?? string.Empty,
        ProjectName = r.GetNullableString("ProjectName"),
        RenewalDate = r.TryGetNullableDateTime("RenewalDate"),
        DueDate = r.TryGetNullableDateTime("DueDate"),
        IsDeleted = !(r.TryGetNullableBoolean("IsActive") ?? true),
        CreatedDate = r.TryGetNullableDateTime("CreatedDate"),
        ModifiedDate = r.TryGetNullableDateTime("ModifiedDate")
    };

    private static Project MapProject(SqlDataReader r) => new()
    {
        ProjectID = r.GetInt32OrDefault("ProjectID"),
        ProjectNumber = r.TryGetNullableString("ProjectNumber") ?? string.Empty,
        ProjectName = r.GetNullableString("ProjectName"),
        ClientID = r.GetInt32OrDefault("ClientID"),
        ClientName = r.TryGetNullableString("ClientName"),
        ClientShortName = r.TryGetNullableString("ClientShortName"),
        BusinessClassID = r.TryGetNullableInt32("BusinessClassID") ?? 0,
        BusinessClassName = BusinessClass.FormatDisplayDescription(
            r.TryGetNullableString("BusinessClassName"),
            r.TryGetNullableString("BusinessClassDescription")),
        BusinessUnitID = r.TryGetNullableInt32("BusinessUnitID"),
        BusinessUnitName = r.TryGetNullableString("BusinessUnitName"),
        UGID = r.GetInt32OrDefault("UGID"),
        UGName = r.TryGetNullableString("UGName"),
        ProjectTypeID = r.GetInt32OrDefault("ProjectTypeID"),
        PTCode = r.TryGetNullableString("PTCode"),
        PTDescription = r.TryGetNullableString("PTDescription"),
        RenewalDate = r.TryGetNullableDateTime("RenewalDate"),
        DueDate = r.TryGetNullableDateTime("DueDate"),
        StatusID = r.GetInt32OrDefault("StatusID"),
        StatusCode = r.TryGetNullableString("StatusCode"),
        StatusDescription = r.TryGetNullableString("StatusDescription"),
        IsOnline = r.TryGetNullableBoolean("IsOnline") ?? false,
        IsFeeBasedRevenue = r.TryGetNullableBoolean("IsFeeBasedRevenue") ?? false,
        IsDeleted = r.TryGetNullableBoolean("IsDeleted") ?? false,
        CreatedDate = r.TryGetNullableDateTime("CreatedDate"),
        ModifiedDate = r.TryGetNullableDateTime("ModifiedDate")
    };

    private static ProjectRevenue MapRevenue(SqlDataReader r) => new()
    {
        RevenueID = r.GetInt32OrDefault("RevenueID"),
        ProjectID = r.GetInt32OrDefault("ProjectID"),
        EffectiveDate = r.GetDateTimeOrDefault("EffectiveDate"),
        Revenue = r.GetNullableDecimal("Revenue") ?? 0m,
        ChangeReason = r.TryGetNullableString("ChangeReason"),
        RecordedBy = r.GetInt32OrDefault("RecordedBy"),
        RecordedByName = r.TryGetNullableString("RecordedByName") ?? r.TryGetNullableString("FullName"),
        CreatedDate = r.GetDateTimeOrDefault("CreatedDate")
    };

    private static ProjectService MapProjectService(SqlDataReader r) => new()
    {
        ProjectID = r.GetInt32OrDefault("ProjectID"),
        ServiceID = r.GetInt32OrDefault("ServiceID"),
        ServiceName = r.TryGetNullableString("ServiceName"),
        CategoryName = r.TryGetNullableString("CategoryName"),
        Frequency = r.TryGetNullableString("Frequency"),
        StartDate = r.TryGetNullableDateTime("StartDate"),
        EndDate = r.TryGetNullableDateTime("EndDate"),
        CreatedDate = r.GetDateTimeOrDefault("CreatedDate")
    };

    private static ProjectNote MapNote(SqlDataReader r) => new()
    {
        NoteID = r.GetInt32OrDefault("NoteID"),
        ProjectID = r.GetInt32OrDefault("ProjectID"),
        UserID = r.TryGetNullableInt32("NoteUserID") ?? 0,
        UserFullName = r.TryGetNullableString("UserFullName") ?? r.TryGetNullableString("FullName"),
        NoteDate = r.GetDateTimeOrDefault("NoteDate"),
        NoteType = r.GetNullableString("NoteType") ?? "General",
        NoteText = r.GetNullableString("NoteText") ?? string.Empty,
        IsPinned = r.GetBooleanOrDefault("IsPinned"),
        CalendarItemID = r.TryGetNullableInt32("CalendarItemID"),
        CalendarItemTitle = r.TryGetNullableString("CalendarItemTitle"),
        ProjectFileID = r.TryGetNullableInt32("ProjectFileID"),
        ProjectFileName = r.TryGetNullableString("ProjectFileName"),
        MentionedUserIDs = r.TryGetNullableString("MentionedUserIDs"),
        CreatedDate = r.GetDateTimeOrDefault("CreatedDate"),
        ModifiedDate = r.TryGetNullableDateTime("ModifiedDate")
    };

    private static ProjectCalendarItem MapCalendarItem(SqlDataReader r) => new()
    {
        CalendarItemID = r.GetInt32OrDefault("CalendarItemID"),
        ProjectID = r.GetInt32OrDefault("ProjectID"),
        ProjectRefID = r.GetInt32OrDefault("ProjectRefID"),
        ItemType = r.GetNullableString("ItemType") ?? "Task",
        Title = r.GetNullableString("Title") ?? string.Empty,
        OwnerUserID = r.TryGetNullableInt32("OwnerUserID"),
        OwnerFullName = r.TryGetNullableString("OwnerFullName"),
        ReceivedDate = r.TryGetNullableDateTime("ReceivedDate"),
        CompletedDate = r.TryGetNullableDateTime("CompletedDate"),
        PercentComplete = Math.Clamp(r.TryGetNullableInt32("PercentComplete") ?? 0, 0, 100),
        Notes = r.TryGetNullableString("Notes"),
        SortOrder = r.GetInt32OrDefault("SortOrder"),
        IsActive = r.TryGetNullableBoolean("IsActive") ?? true,
        CreatedDate = r.TryGetNullableDateTime("CreatedDate"),
        ModifiedDate = r.TryGetNullableDateTime("ModifiedDate")
    };

    private static ProjectTimeEntry MapTimeEntry(SqlDataReader r) => new()
    {
        TimeEntryID = r.GetInt32OrDefault("TimeEntryID"),
        ProjectID = r.GetInt32OrDefault("ProjectID"),
        ProjectRefID = r.GetInt32OrDefault("ProjectRefID"),
        CalendarItemID = r.GetInt32OrDefault("CalendarItemID"),
        CalendarItemTitle = r.TryGetNullableString("CalendarItemTitle"),
        CalendarItemType = r.TryGetNullableString("CalendarItemType"),
        WorkDate = r.GetDateTimeOrDefault("WorkDate"),
        Hours = r.GetNullableDecimal("Hours") ?? 0m,
        EntryMode = r.GetNullableString("EntryMode") ?? "Exact",
        Notes = r.TryGetNullableString("Notes"),
        UserID = r.GetInt32OrDefault("UserID"),
        IsActive = r.TryGetNullableBoolean("IsActive") ?? true,
        CreatedDate = r.TryGetNullableDateTime("CreatedDate"),
        ModifiedDate = r.TryGetNullableDateTime("ModifiedDate")
    };

    private static ProjectFile MapProjectFile(SqlDataReader r) => new()
    {
        ProjectFileID = r.GetInt32OrDefault("ProjectFileID"),
        ProjectID = r.GetInt32OrDefault("ProjectID"),
        ProjectRefID = r.GetInt32OrDefault("ProjectRefID"),
        OriginalFileName = r.GetNullableString("OriginalFileName") ?? string.Empty,
        ContentType = r.TryGetNullableString("ContentType"),
        ByteLength = r.GetInt64OrDefault("ByteLength"),
        BlobPath = r.GetNullableString("BlobPath") ?? string.Empty,
        BlobUrl = r.GetNullableString("BlobUrl") ?? string.Empty,
        Category = r.GetNullableString("Category") ?? "SourceData",
        Description = r.TryGetNullableString("Description"),
        VersionLabel = r.TryGetNullableString("VersionLabel"),
        DataAsOf = r.TryGetNullableDateTime("DataAsOf"),
        IsCurrent = r.TryGetNullableBoolean("IsCurrent") ?? false,
        ApprovalStatus = FileApprovalStatuses.Normalize(r.TryGetNullableString("ApprovalStatus")),
        ApprovalUpdatedByUserID = r.TryGetNullableInt32("ApprovalUpdatedByUserID"),
        ApprovalUpdatedByName = r.TryGetNullableString("ApprovalUpdatedByName"),
        ApprovalUpdatedDate = r.TryGetNullableDateTime("ApprovalUpdatedDate"),
        UploadedByUserID = r.GetInt32OrDefault("UploadedByUserID"),
        UploadedByName = r.TryGetNullableString("UploadedByName"),
        IsActive = r.TryGetNullableBoolean("IsActive") ?? true,
        CreatedDate = r.TryGetNullableDateTime("CreatedDate"),
        ModifiedDate = r.TryGetNullableDateTime("ModifiedDate")
    };

    private static string? NormalizeMentionedUserIds(ProjectNote note)
    {
        var ids = new SortedSet<int>();
        if (note.MentionedUserIdList is { Length: > 0 })
        {
            foreach (var id in note.MentionedUserIdList)
            {
                if (id > 0)
                {
                    ids.Add(id);
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(note.MentionedUserIDs))
        {
            foreach (var part in note.MentionedUserIDs.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) && id > 0)
                {
                    ids.Add(id);
                }
            }
        }

        if (ids.Count == 0)
        {
            return null;
        }

        var joined = string.Join(',', ids);
        return joined.Length > 500 ? joined[..500] : joined;
    }
}
