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
            c.ClientID,
            c.ClientName,
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
            UGID = COALESCE(NULLIF(pref.UserGroupID, 0), projUg.UserGroupID, 0),
            UGName = COALESCE(ugPref.UserGroupName, ug.UserGroupName)
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
          OUTER APPLY (
              SELECT TOP (1) ugr.UserGroupID
                FROM dbo.tProjectUser AS pu
                INNER JOIN dbo.tUserGroupRef AS ugr ON ugr.UserID = pu.UserID
               WHERE pu.ProjectRefID = pref.ProjectRefID AND pu.IsActive = 1
               GROUP BY ugr.UserGroupID
               ORDER BY COUNT(*) DESC, ugr.UserGroupID
          ) AS projUg
          LEFT JOIN dbo.tUserGroup AS ug ON ug.UserGroupID = projUg.UserGroupID
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

    public Task<IReadOnlyList<ProjectNote>> ListNotesAsync(int projectId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT
                pn.NoteID,
                p.ProjectID,
                NoteUserID = CAST(NULL AS INT),
                UserFullName = CAST(NULL AS NVARCHAR(200)),
                pn.NoteDate,
                pn.NoteType,
                pn.NoteText,
                pn.IsPinned,
                pn.CreatedDate,
                pn.ModifiedDate
              FROM dbo.tProjectNote AS pn
              INNER JOIN dbo.tProjectRef AS pr ON pr.ProjectRefID = pn.ProjectRefID AND pr.IsActive = 1
              INNER JOIN dbo.tProject AS p ON p.ProjectID = pr.ProjectID
             WHERE p.ProjectID = @ProjectID
               AND pn.IsActive = 1
             ORDER BY pn.NoteDate DESC, pn.NoteID DESC;
            """;

        return _db.QueryTextAsync(sql, MapNote,
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

        var projectRefId = await ResolveProjectRefIdForProjectAsync(note.ProjectID, cancellationToken);
        var noteDate = note.NoteDate == default ? DateTime.UtcNow : note.NoteDate;
        var output = SqlParameterFactory.OutputInt("@NewNoteID");
        await _db.ExecuteWithOutputIntAsync(
            "dbo.usp_ProjectNote_Create",
            [
                SqlParameterFactory.Int("@ProjectRefID", projectRefId),
                SqlParameterFactory.DateTime2("@NoteDate", noteDate),
                SqlParameterFactory.VarChar("@NoteType", 30, note.NoteType),
                SqlParameterFactory.NVarChar("@NoteText", -1, note.NoteText),
                SqlParameterFactory.Bit("@IsPinned", note.IsPinned),
                SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId),
                output
            ],
            "@NewNoteID",
            cancellationToken);

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
                SqlParameterFactory.Int("@UserGroupID", userGroupId),
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
        CreatedDate = r.GetDateTimeOrDefault("CreatedDate"),
        ModifiedDate = r.TryGetNullableDateTime("ModifiedDate")
    };
}
