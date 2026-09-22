using ClientManagementSystem.Models;
using Microsoft.Data.SqlClient;

namespace ClientManagementSystem.Data;

/// <summary>Read-only linkage audits across clients, projects, user groups, and users.</summary>
public sealed class DataAuditRepository : IDataAuditRepository
{
    /// <summary>dbo.tUserRole.UserRoleID for Analytics Lead.</summary>
    private const int AnalyticsLeadRoleId = 5;

    /// <summary>dbo.tUserRole.UserRoleID for Account Executive.</summary>
    private const int AccountExecutiveRoleId = 8;

    private readonly IDbExecutor _db;
    private readonly IAuthRepository _auth;

    public DataAuditRepository(IDbExecutor db, IAuthRepository auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<DataAuditReport> GetReportAsync(CancellationToken cancellationToken = default)
    {
        await _auth.EnsureLoginPwSchemaAsync(cancellationToken);

        var checks = new List<DataAuditCheck>
        {
            await BuildCheckAsync(
                "clients-missing-renewal-project",
                "Clients with no renewal project",
                "Active clients that have no active project with project type Renewal (RNWL).",
                """
                SELECT
                    CAST(c.ClientID AS nvarchar(20)),
                    ISNULL(NULLIF(LTRIM(RTRIM(c.ClientName)), N''), N'-'),
                    ISNULL(NULLIF(LTRIM(RTRIM(c.ClientShortName)), N''), N'-'),
                    ISNULL(NULLIF(LTRIM(RTRIM(c.ClientStatus)), N''), N'-')
                  FROM dbo.tClient AS c
                 WHERE c.IsActive = 1
                   AND NOT EXISTS (
                           SELECT 1
                             FROM dbo.tClientRef AS cr
                            INNER JOIN dbo.tProjectRef AS pr
                               ON pr.ClientRefID = cr.ClientRefID
                              AND pr.IsActive = 1
                            INNER JOIN dbo.tProject AS p
                               ON p.ProjectID = pr.ProjectID
                              AND p.IsActive = 1
                            INNER JOIN dbo.tProjectType AS pt
                               ON pt.ProjectTypeID = pr.ProjectTypeID
                            WHERE cr.ClientID = c.ClientID
                              AND cr.IsActive = 1
                              AND UPPER(LTRIM(RTRIM(pt.ProjectTypeCode))) = N'RNWL'
                       )
                 ORDER BY c.ClientName, c.ClientID;
                """,
                ["ClientID", "Client", "Short Name", "Status"],
                cancellationToken),

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
                "projects-missing-renewal-date",
                "Projects with no renewal date",
                "Active projects whose RenewalDate is not set.",
                """
                SELECT
                    CAST(p.ProjectID AS nvarchar(20)),
                    ISNULL(NULLIF(LTRIM(RTRIM(p.ProjectNumber)), N''), N'-'),
                    ISNULL(NULLIF(LTRIM(RTRIM(p.ProjectName)), N''), N'-'),
                    ISNULL(NULLIF(LTRIM(RTRIM(c.ClientName)), N''), N'-'),
                    ISNULL(NULLIF(LTRIM(RTRIM(pt.ProjectTypeCode)), N''), N'-')
                  FROM dbo.tProject AS p
                 INNER JOIN dbo.tProjectRef AS pr
                    ON pr.ProjectID = p.ProjectID
                   AND pr.IsActive = 1
                  LEFT JOIN dbo.tProjectType AS pt ON pt.ProjectTypeID = pr.ProjectTypeID
                  LEFT JOIN dbo.tClientRef AS cr ON cr.ClientRefID = pr.ClientRefID
                  LEFT JOIN dbo.tClient AS c ON c.ClientID = cr.ClientID
                 WHERE p.IsActive = 1
                   AND p.RenewalDate IS NULL
                 ORDER BY c.ClientName, p.ProjectName, p.ProjectID;
                """,
                ["ProjectID", "Project #", "Project", "Client", "Type"],
                cancellationToken),

            await BuildCheckAsync(
                "projects-missing-revenue",
                "Projects with no revenue",
                "Active projects that have no active rows in tProjectRevenue.",
                """
                SELECT
                    CAST(p.ProjectID AS nvarchar(20)),
                    ISNULL(NULLIF(LTRIM(RTRIM(p.ProjectNumber)), N''), N'-'),
                    ISNULL(NULLIF(LTRIM(RTRIM(p.ProjectName)), N''), N'-'),
                    ISNULL(NULLIF(LTRIM(RTRIM(c.ClientName)), N''), N'-'),
                    ISNULL(NULLIF(LTRIM(RTRIM(pt.ProjectTypeCode)), N''), N'-')
                  FROM dbo.tProject AS p
                 INNER JOIN dbo.tProjectRef AS pr
                    ON pr.ProjectID = p.ProjectID
                   AND pr.IsActive = 1
                  LEFT JOIN dbo.tProjectType AS pt ON pt.ProjectTypeID = pr.ProjectTypeID
                  LEFT JOIN dbo.tClientRef AS cr ON cr.ClientRefID = pr.ClientRefID
                  LEFT JOIN dbo.tClient AS c ON c.ClientID = cr.ClientID
                 WHERE p.IsActive = 1
                   AND NOT EXISTS (
                           SELECT 1
                             FROM dbo.tProjectRevenue AS rev
                            WHERE rev.ProjectRefID = pr.ProjectRefID
                              AND rev.IsActive = 1
                       )
                 ORDER BY c.ClientName, p.ProjectName, p.ProjectID;
                """,
                ["ProjectID", "Project #", "Project", "Client", "Type"],
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
                "user-groups-without-members",
                "User Groups without members",
                "Active user groups that have no rows in tUserGroupRef.",
                """
                SELECT
                    CAST(ug.UserGroupID AS nvarchar(20)),
                    ISNULL(NULLIF(LTRIM(RTRIM(ug.UserGroupNumber)), N''), N'-'),
                    ISNULL(NULLIF(LTRIM(RTRIM(ug.UserGroupName)), N''), N'-'),
                    CASE WHEN ug.IsActive = 1 THEN N'Active' ELSE N'Inactive' END
                  FROM dbo.tUserGroup AS ug
                 WHERE ug.IsActive = 1
                   AND NOT EXISTS (
                           SELECT 1
                             FROM dbo.tUserGroupRef AS ugr
                            WHERE ugr.UserGroupID = ug.UserGroupID
                       )
                 ORDER BY ug.UserGroupName, ug.UserGroupID;
                """,
                ["UGID", "Group #", "Group", "Status"],
                cancellationToken),

            await BuildCheckAsync(
                "user-groups-cat-users-without-membership",
                "CAT users with no user group",
                "Active CAT department users who are not members of any active user group.",
                """
                SELECT
                    CAST(u.UserID AS nvarchar(20)),
                    LTRIM(RTRIM(CONCAT(u.FirstName, N' ', u.LastName))),
                    ISNULL(NULLIF(LTRIM(RTRIM(u.Email)), N''), N'-'),
                    ISNULL(NULLIF(LTRIM(RTRIM(u.Title)), N''), N'-'),
                    ISNULL(NULLIF(LTRIM(RTRIM(u.Department)), N''), N'-')
                  FROM dbo.tUser AS u
                 WHERE u.IsActive = 1
                   AND UPPER(LTRIM(RTRIM(u.Department))) = N'CAT'
                   AND NOT EXISTS (
                           SELECT 1
                             FROM dbo.tUserGroupRef AS ugr
                            INNER JOIN dbo.tUserGroup AS ug
                               ON ug.UserGroupID = ugr.UserGroupID
                              AND ug.IsActive = 1
                            WHERE ugr.UserID = u.UserID
                       )
                 ORDER BY u.LastName, u.FirstName, u.UserID;
                """,
                ["UserID", "Name", "Email", "Title", "Department"],
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
                "user-groups-missing-account-executive",
                "User groups with no Account Executive",
                "Active user groups that have no member with the Account Executive role.",
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
                              AND ugr.UserRoleID = @AccountExecutiveRoleId
                       )
                 ORDER BY ug.UserGroupName, ug.UserGroupID;
                """,
                ["UGID", "Group #", "Group", "Members"],
                cancellationToken,
                [SqlParameterFactory.Int("@AccountExecutiveRoleId", AccountExecutiveRoleId)]),

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

            await BuildLoginActivityCheckAsync(cancellationToken),
        };

        return new DataAuditReport
        {
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            Checks = checks
        };
    }

    private async Task<DataAuditCheck> BuildLoginActivityCheckAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                CAST(u.UserID AS nvarchar(20)),
                LTRIM(RTRIM(CONCAT(u.FirstName, N' ', u.LastName))),
                ISNULL(NULLIF(LTRIM(RTRIM(u.Email)), N''), N'-'),
                CAST(ISNULL(u.LoginCount, 0) AS nvarchar(20)),
                CASE
                    WHEN u.LastLoginUtc IS NULL THEN N'-'
                    ELSE CONVERT(nvarchar(30), u.LastLoginUtc, 120)
                END
              FROM dbo.tUser AS u
             WHERE u.IsActive = 1
               AND UPPER(LTRIM(RTRIM(u.Department))) = N'CAT'
               AND NULLIF(LTRIM(RTRIM(u.Email)), N'') IS NOT NULL
             ORDER BY ISNULL(u.LoginCount, 0) DESC, u.LastName, u.FirstName, u.UserID;
            """;

        var columns = new[] { "UserID", "Name", "Email", "Login Count", "Last Login (UTC)" };
        var rows = await _db.QueryTextAsync(
            sql,
            r =>
            {
                var cells = new string?[columns.Length];
                for (var i = 0; i < columns.Length; i++)
                {
                    cells[i] = r.IsDBNull(i) ? null : Convert.ToString(r.GetValue(i));
                }

                return (IReadOnlyList<string?>)cells;
            },
            cancellationToken: cancellationToken);

        var totalLogins = 0;
        foreach (var row in rows)
        {
            if (int.TryParse((row.Count > 3 ? row[3] : null) ?? "0", out var n) && n > 0)
            {
                totalLogins += n;
            }
        }

        return new DataAuditCheck
        {
            CheckId = "user-logins-activity",
            Title = "User login counts",
            Description =
                "CAT department sign-ins. Login Count increments on each successful launch; Last Login is UTC.",
            Count = totalLogins,
            Columns = columns,
            Rows = rows,
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
