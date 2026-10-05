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

        var checks = new List<DataAuditCheck>();
        foreach (var descriptor in ListCheckCatalog())
        {
            var check = await GetCheckAsync(descriptor.CheckId, cancellationToken);
            if (check is not null)
            {
                checks.Add(check);
            }
        }

        return new DataAuditReport
        {
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            Checks = checks
        };
    }

    public IReadOnlyList<DataAuditCheckDescriptor> ListCheckCatalog() =>
    [
        new()
        {
            CheckId = "clients-missing-project",
            ToolName = "data_audit_clients_missing_project",
            Title = "Clients with no project",
            Description = "Active clients (ClientStatus = Active) that have no active project of any type.",
        },
        new()
        {
            CheckId = "projects-missing-user-group",
            ToolName = "data_audit_projects_missing_user_group",
            Title = "Projects with no user group",
            Description = "Active projects whose active project ref has no UserGroupID.",
        },
        new()
        {
            CheckId = "projects-missing-renewal-date",
            ToolName = "data_audit_projects_missing_renewal_date",
            Title = "Projects with no renewal date",
            Description = "Active projects whose RenewalDate is not set.",
        },
        new()
        {
            CheckId = "projects-missing-revenue",
            ToolName = "data_audit_projects_missing_revenue",
            Title = "Projects with no revenue",
            Description = "Active renewal (RNWL) projects that have no active rows in tProjectRevenue.",
        },
        new()
        {
            CheckId = "projects-inactive-or-missing-user-group",
            ToolName = "data_audit_projects_inactive_or_missing_user_group",
            Title = "Projects pointing at inactive or missing user groups",
            Description = "Active projects whose UserGroupID is missing from tUserGroup or the group is inactive.",
        },
        new()
        {
            CheckId = "user-groups-without-members",
            ToolName = "data_audit_user_groups_without_members",
            Title = "User Groups without members",
            Description = "Active user groups that have no rows in tUserGroupRef.",
        },
        new()
        {
            CheckId = "user-groups-cat-users-without-membership",
            ToolName = "data_audit_user_groups_cat_users_without_membership",
            Title = "CAT users with no user group",
            Description = "Active CAT department users (excluding Administrators and Andy Siffert) who are not members of any active user group.",
        },
        new()
        {
            CheckId = "user-groups-missing-project",
            ToolName = "data_audit_user_groups_missing_project",
            Title = "User groups with no project",
            Description = "User groups not referenced by any project ref (active or inactive).",
        },
        new()
        {
            CheckId = "user-groups-missing-account-executive",
            ToolName = "data_audit_user_groups_missing_account_executive",
            Title = "User groups with no Account Executive",
            Description = "Active user groups that have no member with the Account Executive role.",
        },
        new()
        {
            CheckId = "user-groups-missing-analytics-lead",
            ToolName = "data_audit_user_groups_missing_analytics_lead",
            Title = "User groups with no Analytics Lead",
            Description = "Active user groups that have no member with the Analytics Lead role.",
        },
        new()
        {
            CheckId = "user-logins-activity",
            ToolName = "data_audit_user_logins_activity",
            Title = "User login counts",
            Description = "CAT department sign-ins. Login Count increments on each successful launch; Last Login is UTC.",
        },
    ];

    public async Task<DataAuditCheck?> GetCheckAsync(string checkId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(checkId))
        {
            return null;
        }

        await _auth.EnsureLoginPwSchemaAsync(cancellationToken);

        return checkId.Trim().ToLowerInvariant() switch
        {
            "clients-missing-project" => await BuildCheckAsync(
                "clients-missing-project",
                "Clients with no project",
                "Active clients (ClientStatus = Active) that have no active project of any type.",
                """
                SELECT
                    CAST(c.ClientID AS nvarchar(20)),
                    ISNULL(NULLIF(LTRIM(RTRIM(c.ClientName)), N''), N'-'),
                    ISNULL(NULLIF(LTRIM(RTRIM(c.ClientShortName)), N''), N'-'),
                    ISNULL(NULLIF(LTRIM(RTRIM(c.ClientStatus)), N''), N'-')
                  FROM dbo.tClient AS c
                 WHERE c.IsActive = 1
                   AND UPPER(LTRIM(RTRIM(ISNULL(c.ClientStatus, N'')))) = N'ACTIVE'
                   AND NOT EXISTS (
                           SELECT 1
                             FROM dbo.tClientRef AS cr
                            INNER JOIN dbo.tProjectRef AS pr
                               ON pr.ClientRefID = cr.ClientRefID
                              AND pr.IsActive = 1
                            INNER JOIN dbo.tProject AS p
                               ON p.ProjectID = pr.ProjectID
                              AND p.IsActive = 1
                            WHERE cr.ClientID = c.ClientID
                              AND cr.IsActive = 1
                       )
                 ORDER BY c.ClientName, c.ClientID;
                """,
                ["ClientID", "Client", "Short Name", "Status"],
                cancellationToken),

            "projects-missing-user-group" => await BuildCheckAsync(
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

            "projects-missing-renewal-date" => await BuildCheckAsync(
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

            "projects-missing-revenue" => await BuildCheckAsync(
                "projects-missing-revenue",
                "Projects with no revenue",
                "Active renewal (RNWL) projects that have no active rows in tProjectRevenue.",
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
                 INNER JOIN dbo.tProjectType AS pt
                    ON pt.ProjectTypeID = pr.ProjectTypeID
                  LEFT JOIN dbo.tClientRef AS cr ON cr.ClientRefID = pr.ClientRefID
                  LEFT JOIN dbo.tClient AS c ON c.ClientID = cr.ClientID
                 WHERE p.IsActive = 1
                   AND UPPER(LTRIM(RTRIM(pt.ProjectTypeCode))) = N'RNWL'
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

            "projects-inactive-or-missing-user-group" => await BuildCheckAsync(
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

            "user-groups-without-members" => await BuildCheckAsync(
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

            "user-groups-cat-users-without-membership" => await BuildCheckAsync(
                "user-groups-cat-users-without-membership",
                "CAT users with no user group",
                "Active CAT department users (excluding Administrators and Andy Siffert) who are not members of any active user group.",
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
                   AND UPPER(LTRIM(RTRIM(ISNULL(u.UserType, N'')))) <> N'ADMINISTRATOR'
                   AND NOT (
                           UPPER(LTRIM(RTRIM(ISNULL(u.LastName, N'')))) = N'SIFFERT'
                       AND (
                               UPPER(LTRIM(RTRIM(ISNULL(u.FirstName, N'')))) IN (N'ANDY', N'ANDREW')
                            OR LOWER(LTRIM(RTRIM(ISNULL(u.Email, N'')))) LIKE N'andrew.siffert%'
                           )
                       )
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

            "user-groups-missing-project" => await BuildCheckAsync(
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

            "user-groups-missing-account-executive" => await BuildCheckAsync(
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

            "user-groups-missing-analytics-lead" => await BuildCheckAsync(
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

            "user-logins-activity" => await BuildLoginActivityCheckAsync(cancellationToken),

            _ => null,
        };
    }

    public async Task<DataAuditExportReport> GetExportAsync(CancellationToken cancellationToken = default)
    {
        var report = await GetReportAsync(cancellationToken);
        var sections = BuildExportSections(report.Checks);
        var byId = report.Checks.ToDictionary(c => c.CheckId, StringComparer.OrdinalIgnoreCase);
        var attributed = await CollectAttributedFindingsCoreAsync(byId, cancellationToken);
        var byLead = GroupAttributedByLead(attributed);

        return new DataAuditExportReport
        {
            GeneratedAtUtc = report.GeneratedAtUtc,
            Sections = sections,
            ByAnalyticsLead = byLead,
        };
    }

    private static IReadOnlyList<DataAuditExportSection> BuildExportSections(IReadOnlyList<DataAuditCheck> checks)
    {
        var order = new[]
        {
            ("clients", "Clients"),
            ("projects", "Projects"),
            ("user-groups", "User groups"),
            ("logins", "Logins"),
            ("other", "Other"),
        };

        var buckets = new Dictionary<string, List<DataAuditCheck>>(StringComparer.OrdinalIgnoreCase);
        foreach (var check in checks)
        {
            var key = ExportSectionKey(check);
            if (!buckets.TryGetValue(key, out var list))
            {
                list = [];
                buckets[key] = list;
            }

            list.Add(check);
        }

        var sections = new List<DataAuditExportSection>();
        foreach (var (key, label) in order)
        {
            if (!buckets.TryGetValue(key, out var list) || list.Count == 0)
            {
                continue;
            }

            var summaries = list.Select(c => new DataAuditExportCheckSummary
            {
                CheckId = c.CheckId,
                Title = c.Title,
                Description = c.Description,
                Count = c.Count,
            }).ToList();

            sections.Add(new DataAuditExportSection
            {
                Key = key,
                Label = label,
                FindingTotal = summaries.Sum(s => s.Count),
                Checks = summaries,
            });
        }

        return sections;
    }

    private static string ExportSectionKey(DataAuditCheck check)
    {
        var id = (check.CheckId ?? string.Empty).ToLowerInvariant();
        var title = (check.Title ?? string.Empty).Trim().ToLowerInvariant();
        if (id.StartsWith("clients-", StringComparison.Ordinal) || title.StartsWith("clients ", StringComparison.Ordinal))
        {
            return "clients";
        }

        if (id.StartsWith("projects-", StringComparison.Ordinal) || title.StartsWith("projects ", StringComparison.Ordinal))
        {
            return "projects";
        }

        if (id.StartsWith("user-groups-", StringComparison.Ordinal) || title.StartsWith("user groups ", StringComparison.Ordinal))
        {
            return "user-groups";
        }

        if (id.StartsWith("user-logins-", StringComparison.Ordinal) || title.StartsWith("user login", StringComparison.Ordinal))
        {
            return "logins";
        }

        return "other";
    }

    private async Task<List<AttributedFinding>> CollectAttributedFindingsCoreAsync(
        Dictionary<string, DataAuditCheck> byId,
        CancellationToken cancellationToken)
    {
        var attributed = new List<AttributedFinding>();

        async Task CollectAsync(
            string checkId,
            string sectionKey,
            string sectionLabel,
            string sql,
            IEnumerable<SqlParameter>? parameters = null,
            bool forceUnassigned = false)
        {
            if (!byId.TryGetValue(checkId, out var check) || check.Count <= 0)
            {
                return;
            }

            var rows = await QueryAttributedAsync(sql, parameters, cancellationToken);
            foreach (var row in rows)
            {
                int? alId = forceUnassigned ? null : row.AnalyticsLeadUserID;
                if (alId is <= 0)
                {
                    alId = null;
                }

                var name = alId is null
                    ? "Unassigned"
                    : (string.IsNullOrWhiteSpace(row.AnalyticsLeadName)
                        ? "Unassigned"
                        : row.AnalyticsLeadName.Trim());
                if (string.Equals(name, "Unassigned", StringComparison.OrdinalIgnoreCase))
                {
                    alId = null;
                }

                attributed.Add(new AttributedFinding
                {
                    SectionKey = sectionKey,
                    SectionLabel = sectionLabel,
                    CheckId = check.CheckId,
                    CheckTitle = check.Title,
                    EntityLabel = string.IsNullOrWhiteSpace(row.EntityLabel) ? "-" : row.EntityLabel.Trim(),
                    AnalyticsLeadUserID = alId,
                    AnalyticsLeadName = alId is null ? "Unassigned" : name,
                    AnalyticsLeadEmail = alId is null ? null : NullIfBlank(row.AnalyticsLeadEmail),
                });
            }
        }

        const string clientAlSelect = """
                    ISNULL(NULLIF(LTRIM(RTRIM(c.ClientName)), N''), N'-') AS EntityLabel,
                    c.AnalyticsLeadUserID AS AlUserID,
                    NULLIF(LTRIM(RTRIM(CONCAT(ISNULL(al.FirstName, N''), N' ', ISNULL(al.LastName, N'')))), N'') AS AlName,
                    NULLIF(LTRIM(RTRIM(al.Email)), N'') AS AlEmail
            """;

        const string projectEntity = """
                    LTRIM(RTRIM(CONCAT(
                        ISNULL(NULLIF(LTRIM(RTRIM(p.ProjectName)), N''), N'Project'),
                        CASE
                            WHEN NULLIF(LTRIM(RTRIM(c.ClientName)), N'') IS NULL THEN N''
                            ELSE CONCAT(N' (', LTRIM(RTRIM(c.ClientName)), N')')
                        END
                    ))) AS EntityLabel,
                    c.AnalyticsLeadUserID AS AlUserID,
                    NULLIF(LTRIM(RTRIM(CONCAT(ISNULL(al.FirstName, N''), N' ', ISNULL(al.LastName, N'')))), N'') AS AlName,
                    NULLIF(LTRIM(RTRIM(al.Email)), N'') AS AlEmail
            """;

        const string ugEntityWithAl = """
                    ISNULL(NULLIF(LTRIM(RTRIM(ug.UserGroupName)), N''), N'-') AS EntityLabel,
                    al.UserID AS AlUserID,
                    NULLIF(LTRIM(RTRIM(CONCAT(ISNULL(al.FirstName, N''), N' ', ISNULL(al.LastName, N'')))), N'') AS AlName,
                    NULLIF(LTRIM(RTRIM(al.Email)), N'') AS AlEmail
            """;

        const string ugAlApply = """
                  OUTER APPLY (
                        SELECT TOP (1)
                               u.UserID,
                               u.FirstName,
                               u.LastName,
                               u.Email
                          FROM dbo.tUserGroupRef AS ugr
                         INNER JOIN dbo.tUser AS u ON u.UserID = ugr.UserID
                         WHERE ugr.UserGroupID = ug.UserGroupID
                           AND ugr.UserRoleID = @AnalyticsLeadRoleId
                         ORDER BY u.UserID
                  ) AS al
            """;

        await CollectAsync(
            "clients-missing-project",
            "clients",
            "Clients",
            $"""
            SELECT
                {clientAlSelect}
              FROM dbo.tClient AS c
              LEFT JOIN dbo.tUser AS al ON al.UserID = c.AnalyticsLeadUserID
             WHERE c.IsActive = 1
               AND UPPER(LTRIM(RTRIM(ISNULL(c.ClientStatus, N'')))) = N'ACTIVE'
               AND NOT EXISTS (
                       SELECT 1
                         FROM dbo.tClientRef AS cr
                        INNER JOIN dbo.tProjectRef AS pr
                           ON pr.ClientRefID = cr.ClientRefID
                          AND pr.IsActive = 1
                        INNER JOIN dbo.tProject AS p
                           ON p.ProjectID = pr.ProjectID
                          AND p.IsActive = 1
                        WHERE cr.ClientID = c.ClientID
                          AND cr.IsActive = 1
                   )
             ORDER BY c.ClientName, c.ClientID;
            """);

        await CollectAsync(
            "projects-missing-user-group",
            "projects",
            "Projects",
            $"""
            SELECT
                {projectEntity}
              FROM dbo.tProject AS p
             INNER JOIN dbo.tProjectRef AS pr
                ON pr.ProjectID = p.ProjectID
               AND pr.IsActive = 1
              LEFT JOIN dbo.tClientRef AS cr ON cr.ClientRefID = pr.ClientRefID
              LEFT JOIN dbo.tClient AS c ON c.ClientID = cr.ClientID
              LEFT JOIN dbo.tUser AS al ON al.UserID = c.AnalyticsLeadUserID
             WHERE p.IsActive = 1
               AND pr.UserGroupID IS NULL
             ORDER BY c.ClientName, p.ProjectName, p.ProjectID;
            """);

        await CollectAsync(
            "projects-missing-renewal-date",
            "projects",
            "Projects",
            $"""
            SELECT
                {projectEntity}
              FROM dbo.tProject AS p
             INNER JOIN dbo.tProjectRef AS pr
                ON pr.ProjectID = p.ProjectID
               AND pr.IsActive = 1
              LEFT JOIN dbo.tClientRef AS cr ON cr.ClientRefID = pr.ClientRefID
              LEFT JOIN dbo.tClient AS c ON c.ClientID = cr.ClientID
              LEFT JOIN dbo.tUser AS al ON al.UserID = c.AnalyticsLeadUserID
             WHERE p.IsActive = 1
               AND p.RenewalDate IS NULL
             ORDER BY c.ClientName, p.ProjectName, p.ProjectID;
            """);

        await CollectAsync(
            "projects-missing-revenue",
            "projects",
            "Projects",
            $"""
            SELECT
                {projectEntity}
              FROM dbo.tProject AS p
             INNER JOIN dbo.tProjectRef AS pr
                ON pr.ProjectID = p.ProjectID
               AND pr.IsActive = 1
             INNER JOIN dbo.tProjectType AS pt
                ON pt.ProjectTypeID = pr.ProjectTypeID
              LEFT JOIN dbo.tClientRef AS cr ON cr.ClientRefID = pr.ClientRefID
              LEFT JOIN dbo.tClient AS c ON c.ClientID = cr.ClientID
              LEFT JOIN dbo.tUser AS al ON al.UserID = c.AnalyticsLeadUserID
             WHERE p.IsActive = 1
               AND UPPER(LTRIM(RTRIM(pt.ProjectTypeCode))) = N'RNWL'
               AND NOT EXISTS (
                       SELECT 1
                         FROM dbo.tProjectRevenue AS rev
                        WHERE rev.ProjectRefID = pr.ProjectRefID
                          AND rev.IsActive = 1
                   )
             ORDER BY c.ClientName, p.ProjectName, p.ProjectID;
            """);

        await CollectAsync(
            "projects-inactive-or-missing-user-group",
            "projects",
            "Projects",
            $"""
            SELECT
                {projectEntity}
              FROM dbo.tProject AS p
             INNER JOIN dbo.tProjectRef AS pr
                ON pr.ProjectID = p.ProjectID
               AND pr.IsActive = 1
              LEFT JOIN dbo.tUserGroup AS ug ON ug.UserGroupID = pr.UserGroupID
              LEFT JOIN dbo.tClientRef AS cr ON cr.ClientRefID = pr.ClientRefID
              LEFT JOIN dbo.tClient AS c ON c.ClientID = cr.ClientID
              LEFT JOIN dbo.tUser AS al ON al.UserID = c.AnalyticsLeadUserID
             WHERE p.IsActive = 1
               AND pr.UserGroupID IS NOT NULL
               AND (ug.UserGroupID IS NULL OR ug.IsActive = 0)
             ORDER BY c.ClientName, p.ProjectName, p.ProjectID;
            """);

        var alParam = new[] { SqlParameterFactory.Int("@AnalyticsLeadRoleId", AnalyticsLeadRoleId) };

        await CollectAsync(
            "user-groups-without-members",
            "user-groups",
            "User groups",
            $"""
            SELECT
                {ugEntityWithAl}
              FROM dbo.tUserGroup AS ug
            {ugAlApply}
             WHERE ug.IsActive = 1
               AND NOT EXISTS (
                       SELECT 1
                         FROM dbo.tUserGroupRef AS ugr
                        WHERE ugr.UserGroupID = ug.UserGroupID
                   )
             ORDER BY ug.UserGroupName, ug.UserGroupID;
            """,
            alParam);

        await CollectAsync(
            "user-groups-cat-users-without-membership",
            "user-groups",
            "User groups",
            """
            SELECT
                LTRIM(RTRIM(CONCAT(u.FirstName, N' ', u.LastName))) AS EntityLabel,
                CAST(NULL AS int) AS AlUserID,
                CAST(NULL AS nvarchar(200)) AS AlName,
                CAST(NULL AS nvarchar(200)) AS AlEmail
              FROM dbo.tUser AS u
             WHERE u.IsActive = 1
               AND UPPER(LTRIM(RTRIM(u.Department))) = N'CAT'
               AND UPPER(LTRIM(RTRIM(ISNULL(u.UserType, N'')))) <> N'ADMINISTRATOR'
               AND NOT (
                       UPPER(LTRIM(RTRIM(ISNULL(u.LastName, N'')))) = N'SIFFERT'
                   AND (
                           UPPER(LTRIM(RTRIM(ISNULL(u.FirstName, N'')))) IN (N'ANDY', N'ANDREW')
                        OR LOWER(LTRIM(RTRIM(ISNULL(u.Email, N'')))) LIKE N'andrew.siffert%'
                       )
                   )
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
            forceUnassigned: true);

        await CollectAsync(
            "user-groups-missing-project",
            "user-groups",
            "User groups",
            $"""
            SELECT
                {ugEntityWithAl}
              FROM dbo.tUserGroup AS ug
            {ugAlApply}
             WHERE NOT EXISTS (
                       SELECT 1
                         FROM dbo.tProjectRef AS pr
                        WHERE pr.UserGroupID = ug.UserGroupID
                   )
             ORDER BY ug.UserGroupName, ug.UserGroupID;
            """,
            alParam);

        await CollectAsync(
            "user-groups-missing-account-executive",
            "user-groups",
            "User groups",
            $"""
            SELECT
                {ugEntityWithAl}
              FROM dbo.tUserGroup AS ug
            {ugAlApply}
             WHERE ug.IsActive = 1
               AND NOT EXISTS (
                       SELECT 1
                         FROM dbo.tUserGroupRef AS ugr
                        WHERE ugr.UserGroupID = ug.UserGroupID
                          AND ugr.UserRoleID = @AccountExecutiveRoleId
                   )
             ORDER BY ug.UserGroupName, ug.UserGroupID;
            """,
            [
                SqlParameterFactory.Int("@AnalyticsLeadRoleId", AnalyticsLeadRoleId),
                SqlParameterFactory.Int("@AccountExecutiveRoleId", AccountExecutiveRoleId),
            ]);

        await CollectAsync(
            "user-groups-missing-analytics-lead",
            "user-groups",
            "User groups",
            """
            SELECT
                ISNULL(NULLIF(LTRIM(RTRIM(ug.UserGroupName)), N''), N'-') AS EntityLabel,
                CAST(NULL AS int) AS AlUserID,
                CAST(NULL AS nvarchar(200)) AS AlName,
                CAST(NULL AS nvarchar(200)) AS AlEmail
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
            alParam,
            forceUnassigned: true);

        return attributed;
    }

    private sealed class AttributedFinding
    {
        public string SectionKey { get; set; } = string.Empty;
        public string SectionLabel { get; set; } = string.Empty;
        public string CheckId { get; set; } = string.Empty;
        public string CheckTitle { get; set; } = string.Empty;
        public string EntityLabel { get; set; } = string.Empty;
        public int? AnalyticsLeadUserID { get; set; }
        public string AnalyticsLeadName { get; set; } = "Unassigned";
        public string? AnalyticsLeadEmail { get; set; }

        public DataAuditExportFinding ToExportFinding() => new()
        {
            SectionKey = SectionKey,
            SectionLabel = SectionLabel,
            CheckId = CheckId,
            CheckTitle = CheckTitle,
            EntityLabel = EntityLabel,
        };
    }

    private sealed class AttributedSqlRow
    {
        public string EntityLabel { get; set; } = string.Empty;
        public int? AnalyticsLeadUserID { get; set; }
        public string? AnalyticsLeadName { get; set; }
        public string? AnalyticsLeadEmail { get; set; }
    }

    private async Task<IReadOnlyList<AttributedSqlRow>> QueryAttributedAsync(
        string sql,
        IEnumerable<SqlParameter>? parameters,
        CancellationToken cancellationToken)
    {
        return await _db.QueryTextAsync(
            sql,
            r =>
            {
                var entity = r.IsDBNull(0) ? "-" : Convert.ToString(r.GetValue(0)) ?? "-";
                int? alId = null;
                if (!r.IsDBNull(1))
                {
                    var raw = Convert.ToInt32(r.GetValue(1));
                    if (raw > 0)
                    {
                        alId = raw;
                    }
                }

                return new AttributedSqlRow
                {
                    EntityLabel = entity,
                    AnalyticsLeadUserID = alId,
                    AnalyticsLeadName = r.IsDBNull(2) ? null : Convert.ToString(r.GetValue(2)),
                    AnalyticsLeadEmail = r.IsDBNull(3) ? null : Convert.ToString(r.GetValue(3)),
                };
            },
            parameters,
            cancellationToken);
    }

    private static string? NullIfBlank(string? value)
    {
        var t = value?.Trim();
        return string.IsNullOrEmpty(t) ? null : t;
    }

    private static IReadOnlyList<DataAuditExportLeadGroup> GroupAttributedByLead(IReadOnlyList<AttributedFinding> attributed)
    {
        var groups = attributed
            .GroupBy(a => a.AnalyticsLeadUserID ?? 0)
            .Select(g =>
            {
                var first = g.First();
                var isUnassigned = first.AnalyticsLeadUserID is null;
                var items = g
                    .OrderBy(x => x.SectionLabel, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.CheckTitle, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.EntityLabel, StringComparer.OrdinalIgnoreCase)
                    .Select(x => x.ToExportFinding())
                    .ToList();

                return new DataAuditExportLeadGroup
                {
                    AnalyticsLeadUserID = isUnassigned ? null : first.AnalyticsLeadUserID,
                    AnalyticsLeadName = isUnassigned ? "Unassigned" : first.AnalyticsLeadName,
                    AnalyticsLeadEmail = isUnassigned ? null : first.AnalyticsLeadEmail,
                    FindingTotal = items.Count,
                    Findings = items,
                };
            })
            .OrderBy(g => g.AnalyticsLeadUserID is null ? 1 : 0)
            .ThenBy(g => g.AnalyticsLeadName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return groups;
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
