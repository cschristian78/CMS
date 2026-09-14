using ClientManagementSystem.Models;
using Microsoft.Data.SqlClient;

namespace ClientManagementSystem.Data;

/// <summary>Read-only linkage audits across clients, projects, user groups, and users.</summary>
public sealed class DataAuditRepository : IDataAuditRepository
{
    /// <summary>dbo.tUserRole.UserRoleID for Analytics Lead.</summary>
    private const int AnalyticsLeadRoleId = 5;

    private readonly IDbExecutor _db;

    public DataAuditRepository(IDbExecutor db)
    {
        _db = db;
    }

    public async Task<DataAuditReport> GetReportAsync(CancellationToken cancellationToken = default)
    {
        var checks = new List<DataAuditCheck>
        {
            await BuildCheckAsync(
                "projects-missing-user-group",
                "Projects with no user group",
                "Active projects whose active project ref has no UserGroupID.",
                """
                SELECT
                    CAST(p.ProjectID AS nvarchar(20)),
                    ISNULL(NULLIF(LTRIM(RTRIM(p.ProjectNumber)), N''), N'-'),
                    ISNULL(NULLIF(LTRIM(RTRIM(p.ProjectName)), N''), N'-'),
                    ISNULL(NULLIF(LTRIM(RTRIM(c.ClientName)), N''), N'-')
                  FROM dbo.tProject AS p
                 INNER JOIN dbo.tProjectRef AS pr
                    ON pr.ProjectID = p.ProjectID
                   AND pr.IsActive = 1
                  LEFT JOIN dbo.tClientRef AS cr ON cr.ClientRefID = pr.ClientRefID
                  LEFT JOIN dbo.tClient AS c ON c.ClientID = cr.ClientID
                 WHERE p.IsActive = 1
                   AND pr.UserGroupID IS NULL
                 ORDER BY c.ClientName, p.ProjectName, p.ProjectID;
                """,
                ["ProjectID", "Project #", "Project", "Client"],
                cancellationToken),

            await BuildCheckAsync(
                "projects-empty-user-group",
                "Projects whose user group has no members",
                "Active projects assigned to a user group that has no rows in tUserGroupRef.",
                """
                SELECT
                    CAST(p.ProjectID AS nvarchar(20)),
                    ISNULL(NULLIF(LTRIM(RTRIM(p.ProjectNumber)), N''), N'-'),
                    ISNULL(NULLIF(LTRIM(RTRIM(p.ProjectName)), N''), N'-'),
                    ISNULL(NULLIF(LTRIM(RTRIM(c.ClientName)), N''), N'-'),
                    CAST(ug.UserGroupID AS nvarchar(20)),
                    ISNULL(NULLIF(LTRIM(RTRIM(ug.UserGroupName)), N''), N'-')
                  FROM dbo.tProject AS p
                 INNER JOIN dbo.tProjectRef AS pr
                    ON pr.ProjectID = p.ProjectID
                   AND pr.IsActive = 1
                 INNER JOIN dbo.tUserGroup AS ug ON ug.UserGroupID = pr.UserGroupID
                  LEFT JOIN dbo.tClientRef AS cr ON cr.ClientRefID = pr.ClientRefID
                  LEFT JOIN dbo.tClient AS c ON c.ClientID = cr.ClientID
                 WHERE p.IsActive = 1
                   AND NOT EXISTS (
                           SELECT 1
                             FROM dbo.tUserGroupRef AS ugr
                            WHERE ugr.UserGroupID = ug.UserGroupID
                       )
                 ORDER BY c.ClientName, p.ProjectName, p.ProjectID;
                """,
                ["ProjectID", "Project #", "Project", "Client", "UGID", "Group"],
                cancellationToken),

            await BuildCheckAsync(
                "projects-inactive-or-missing-user-group",
                "Projects pointing at inactive or missing user groups",
                "Active projects whose UserGroupID is missing from tUserGroup or the group is inactive.",
                """
                SELECT
                    CAST(p.ProjectID AS nvarchar(20)),
                    ISNULL(NULLIF(LTRIM(RTRIM(p.ProjectNumber)), N''), N'-'),
                    ISNULL(NULLIF(LTRIM(RTRIM(p.ProjectName)), N''), N'-'),
                    ISNULL(NULLIF(LTRIM(RTRIM(c.ClientName)), N''), N'-'),
                    CAST(pr.UserGroupID AS nvarchar(20)),
                    CASE
                        WHEN ug.UserGroupID IS NULL THEN N'Missing group'
                        WHEN ug.IsActive = 0 THEN N'Inactive group'
                        ELSE N'-'
                    END,
                    ISNULL(NULLIF(LTRIM(RTRIM(ug.UserGroupName)), N''), N'-')
                  FROM dbo.tProject AS p
                 INNER JOIN dbo.tProjectRef AS pr
                    ON pr.ProjectID = p.ProjectID
                   AND pr.IsActive = 1
                  LEFT JOIN dbo.tUserGroup AS ug ON ug.UserGroupID = pr.UserGroupID
                  LEFT JOIN dbo.tClientRef AS cr ON cr.ClientRefID = pr.ClientRefID
                  LEFT JOIN dbo.tClient AS c ON c.ClientID = cr.ClientID
                 WHERE p.IsActive = 1
                   AND pr.UserGroupID IS NOT NULL
                   AND (ug.UserGroupID IS NULL OR ug.IsActive = 0)
                 ORDER BY c.ClientName, p.ProjectName, p.ProjectID;
                """,
                ["ProjectID", "Project #", "Project", "Client", "UGID", "Issue", "Group"],
                cancellationToken),

            await BuildCheckAsync(
                "user-groups-missing-project",
                "User groups with no project",
                "User groups not referenced by any project ref (active or inactive).",
                """
                SELECT
                    CAST(ug.UserGroupID AS nvarchar(20)),
                    ISNULL(NULLIF(LTRIM(RTRIM(ug.UserGroupNumber)), N''), N'-'),
                    ISNULL(NULLIF(LTRIM(RTRIM(ug.UserGroupName)), N''), N'-'),
                    CASE WHEN ug.IsActive = 1 THEN N'Active' ELSE N'Inactive' END
                  FROM dbo.tUserGroup AS ug
                 WHERE NOT EXISTS (
                           SELECT 1
                             FROM dbo.tProjectRef AS pr
                            WHERE pr.UserGroupID = ug.UserGroupID
                       )
                 ORDER BY ug.UserGroupName, ug.UserGroupID;
                """,
                ["UGID", "Group #", "Group", "Status"],
                cancellationToken),

            await BuildCheckAsync(
                "user-groups-missing-client",
                "User groups with no client",
                "User groups with no client linkage through any project (project → client ref → client).",
                """
                SELECT
                    CAST(ug.UserGroupID AS nvarchar(20)),
                    ISNULL(NULLIF(LTRIM(RTRIM(ug.UserGroupNumber)), N''), N'-'),
                    ISNULL(NULLIF(LTRIM(RTRIM(ug.UserGroupName)), N''), N'-'),
                    CASE WHEN ug.IsActive = 1 THEN N'Active' ELSE N'Inactive' END
                  FROM dbo.tUserGroup AS ug
                 WHERE NOT EXISTS (
                           SELECT 1
                             FROM dbo.tProjectRef AS pr
                            INNER JOIN dbo.tClientRef AS cr ON cr.ClientRefID = pr.ClientRefID
                            INNER JOIN dbo.tClient AS c ON c.ClientID = cr.ClientID
                            WHERE pr.UserGroupID = ug.UserGroupID
                       )
                 ORDER BY ug.UserGroupName, ug.UserGroupID;
                """,
                ["UGID", "Group #", "Group", "Status"],
                cancellationToken),

            await BuildCheckAsync(
                "user-groups-missing-analytics-lead",
                "User groups with no Analytics Lead",
                "Active user groups that have no member with the Analytics Lead role.",
                """
                SELECT
                    CAST(ug.UserGroupID AS nvarchar(20)),
                    ISNULL(NULLIF(LTRIM(RTRIM(ug.UserGroupNumber)), N''), N'-'),
                    ISNULL(NULLIF(LTRIM(RTRIM(ug.UserGroupName)), N''), N'-'),
                    CAST((
                        SELECT COUNT(*)
                          FROM dbo.tUserGroupRef AS ugr
                         WHERE ugr.UserGroupID = ug.UserGroupID
                    ) AS nvarchar(20)) AS MemberCount
                  FROM dbo.tUserGroup AS ug
                 WHERE ug.IsActive = 1
                   AND NOT EXISTS (
                           SELECT 1
                             FROM dbo.tUserGroupRef AS ugr
                            WHERE ugr.UserGroupID = ug.UserGroupID
                              AND ugr.UserRoleID = @AnalyticsLeadRoleId
                       )
                 ORDER BY ug.UserGroupName, ug.UserGroupID;
                """,
                ["UGID", "Group #", "Group", "Members"],
                cancellationToken,
                [SqlParameterFactory.Int("@AnalyticsLeadRoleId", AnalyticsLeadRoleId)]),
        };

        return new DataAuditReport
        {
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            Checks = checks
        };
    }

    private async Task<DataAuditCheck> BuildCheckAsync(
        string checkId,
        string title,
        string description,
        string sql,
        IReadOnlyList<string> columns,
        CancellationToken cancellationToken,
        IEnumerable<SqlParameter>? parameters = null)
    {
        var rows = await _db.QueryTextAsync(
            sql,
            r =>
            {
                var cells = new string?[columns.Count];
                for (var i = 0; i < columns.Count; i++)
                {
                    cells[i] = r.IsDBNull(i) ? null : Convert.ToString(r.GetValue(i));
                }

                return (IReadOnlyList<string?>)cells;
            },
            parameters,
            cancellationToken);

        return new DataAuditCheck
        {
            CheckId = checkId,
            Title = title,
            Description = description,
            Count = rows.Count,
            Columns = columns,
            Rows = rows
        };
    }
}
