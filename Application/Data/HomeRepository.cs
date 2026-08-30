using System.Text;
using ClientManagementSystem.Models;
using Microsoft.Data.SqlClient;

namespace ClientManagementSystem.Data;

/// <summary>Home page roll-ups, sourced from <c>dbo.vProject_Current</c> (which surfaces
/// <c>LatestRevenue</c> via an <c>OUTER APPLY</c> on <c>dbo.tProjectRevenue</c>).</summary>
public sealed class HomeRepository : IHomeRepository
{
    private const string ActiveClientProjectPredicate = """
         AND LTRIM(RTRIM(CAST([StatusDescription] AS NVARCHAR(50)))) = N'Active'
         AND LTRIM(RTRIM(CAST([ProjectStatus] AS NVARCHAR(50)))) = N'Active'
        """;

    private readonly IDbExecutor _db;

    public HomeRepository(IDbExecutor db)
    {
        _db = db;
    }

    public async Task<HomeSummary> GetHomeSummaryAsync(
        string? businessUnitName = null,
        string? statusName = null,
        string? clientNameSearch = null,
        string? businessClassName = null,
        string? projectTypeName = null,
        bool? isOnline = null,
        IReadOnlyList<string>? analyticsLeadNames = null,
        CancellationToken cancellationToken = default)
    {
        var totalsSql = new StringBuilder(
            """
            WITH filtered AS (
                SELECT ClientID, ProjectID, LatestRevenue
                FROM dbo.vProject_Current
                WHERE 1 = 1
            """);

        totalsSql.Append(ActiveClientProjectPredicate);

        // Distinct-project counts still respect the Main list filters (BU / status / online / etc.).
        var parameters = new List<SqlParameter>();
        ClientProjectListFilterSql.AppendFilters(
            totalsSql,
            parameters,
            businessUnitName,
            statusName,
            clientNameSearch,
            businessClassName,
            projectTypeName,
            isOnline,
            analyticsLeadNames: analyticsLeadNames);

        totalsSql.Append(
            """
            )
            SELECT
                CAST(0 AS int) AS UniqueClientCount,
                CAST((
                    SELECT COUNT(DISTINCT f.ProjectID)
                    FROM filtered AS f
                    WHERE f.ProjectID IS NOT NULL
                ) AS int) AS DistinctProjectCount,
                CAST(0 AS float) AS TotalProjectRevenue
            """);

        var totals = await _db.QueryTextAsync(
            totalsSql.ToString(),
            static r => new
            {
                UniqueClientCount = r.GetInt32(r.GetOrdinal("UniqueClientCount")),
                DistinctProjectCount = r.GetInt32(r.GetOrdinal("DistinctProjectCount")),
                TotalProjectRevenue = r.IsDBNull(r.GetOrdinal("TotalProjectRevenue"))
                    ? 0d
                    : r.GetDouble(r.GetOrdinal("TotalProjectRevenue"))
            },
            parameters,
            cancellationToken);

        var row = totals.Count > 0
            ? totals[0]
            : new { UniqueClientCount = 0, DistinctProjectCount = 0, TotalProjectRevenue = 0d };

        // Total Revenue tile: sum latest revenue for ALL Active projects (Active client + Active
        // project). Intentionally ignores Main list filters (including Online) so the tile is not
        // reduced when Online defaults to "Online".
        const string totalRevenueSql = """
            SELECT CAST(ISNULL(SUM(perProject.LatestRevenue), 0) AS float) AS TotalProjectRevenue
            FROM (
                SELECT v.ProjectID, MAX(CAST(v.LatestRevenue AS float)) AS LatestRevenue
                FROM dbo.vProject_Current AS v
                WHERE v.ProjectID IS NOT NULL
                  AND v.LatestRevenue IS NOT NULL
                  AND LTRIM(RTRIM(CAST(v.[StatusDescription] AS NVARCHAR(50)))) = N'Active'
                  AND LTRIM(RTRIM(CAST(v.[ProjectStatus] AS NVARCHAR(50)))) = N'Active'
                GROUP BY v.ProjectID
            ) AS perProject
            """;

        var totalRevenueRows = await _db.QueryTextAsync(
            totalRevenueSql,
            static r => r.IsDBNull(r.GetOrdinal("TotalProjectRevenue"))
                ? 0d
                : r.GetDouble(r.GetOrdinal("TotalProjectRevenue")),
            null,
            cancellationToken);

        var totalProjectRevenue = totalRevenueRows.Count > 0 ? totalRevenueRows[0] : 0d;

        // Same universe as Total Revenue / Online tiles:
        // distinct Active projects in dbo.vProject_Current (Active client + Active project).
        const string activeProjectCountSql = """
            SELECT CAST(COUNT(DISTINCT v.ProjectID) AS int) AS ActiveProjectCount
              FROM dbo.vProject_Current AS v
             WHERE v.ProjectID IS NOT NULL
               AND LTRIM(RTRIM(CAST(v.[StatusDescription] AS NVARCHAR(50)))) = N'Active'
               AND LTRIM(RTRIM(CAST(v.[ProjectStatus] AS NVARCHAR(50)))) = N'Active'
            """;

        var activeProjectCountRows = await _db.QueryTextAsync(
            activeProjectCountSql,
            static r => r.GetInt32(r.GetOrdinal("ActiveProjectCount")),
            null,
            cancellationToken);

        var activeProjectCount = activeProjectCountRows.Count > 0 ? activeProjectCountRows[0] : 0;

        // Global online roll-ups (same Active client/project scope as revenue; ignore list filters
        // so the Online Projects tile is not zeroed by the Online/Offline dropdown).
        const string onlineTotalsSql = """
            SELECT
                CAST((
                    SELECT COUNT(DISTINCT v.ProjectID)
                    FROM dbo.vProject_Current AS v
                    INNER JOIN dbo.tProject AS p ON p.ProjectID = v.ProjectID
                    WHERE v.ProjectID IS NOT NULL
                      AND ISNULL(p.IsOnline, 0) = 1
                      AND LTRIM(RTRIM(CAST(v.[StatusDescription] AS NVARCHAR(50)))) = N'Active'
                      AND LTRIM(RTRIM(CAST(v.[ProjectStatus] AS NVARCHAR(50)))) = N'Active'
                ) AS int) AS OnlineProjectCount,
                CAST(ISNULL((
                    SELECT SUM(CAST(v.LatestRevenue AS float))
                    FROM dbo.vProject_Current AS v
                    INNER JOIN dbo.tProject AS p ON p.ProjectID = v.ProjectID
                    WHERE v.ProjectID IS NOT NULL
                      AND ISNULL(p.IsOnline, 0) = 1
                      AND v.LatestRevenue IS NOT NULL
                      AND LTRIM(RTRIM(CAST(v.[StatusDescription] AS NVARCHAR(50)))) = N'Active'
                      AND LTRIM(RTRIM(CAST(v.[ProjectStatus] AS NVARCHAR(50)))) = N'Active'
                ), 0) AS float) AS OnlineProjectRevenue
            """;

        var onlineTotals = await _db.QueryTextAsync(
            onlineTotalsSql,
            static r => new
            {
                OnlineProjectCount = r.GetInt32(r.GetOrdinal("OnlineProjectCount")),
                OnlineProjectRevenue = r.IsDBNull(r.GetOrdinal("OnlineProjectRevenue"))
                    ? 0d
                    : r.GetDouble(r.GetOrdinal("OnlineProjectRevenue"))
            },
            null,
            cancellationToken);

        var onlineRow = onlineTotals.Count > 0
            ? onlineTotals[0]
            : new { OnlineProjectCount = 0, OnlineProjectRevenue = 0d };

        var byTypeSql = new StringBuilder(
            """
            WITH filtered AS (
                SELECT ProjectID, PTDescription
                FROM dbo.vProject_Current
                WHERE ProjectID IS NOT NULL
            """);

        byTypeSql.Append(ActiveClientProjectPredicate);

        var byTypeParams = new List<SqlParameter>();
        ClientProjectListFilterSql.AppendFilters(
            byTypeSql,
            byTypeParams,
            businessUnitName,
            statusName,
            clientNameSearch,
            businessClassName,
            projectTypeName,
            isOnline,
            analyticsLeadNames: analyticsLeadNames);

        byTypeSql.Append(
            """
            )
            SELECT
                CAST(ISNULL(NULLIF(LTRIM(RTRIM(CAST(f.PTDescription AS NVARCHAR(255)))), N''), N'(Unspecified)') AS nvarchar(255)) AS ProjectTypeDescription,
                CAST(COUNT(DISTINCT f.ProjectID) AS int) AS ProjectCount
            FROM filtered AS f
            GROUP BY ISNULL(NULLIF(LTRIM(RTRIM(CAST(f.PTDescription AS NVARCHAR(255)))), N''), N'(Unspecified)')
            ORDER BY ProjectTypeDescription
            """);

        var byType = await _db.QueryTextAsync(
            byTypeSql.ToString(),
            static r => new HomeProjectTypeCount
            {
                ProjectTypeDescription = r.GetNullableString("ProjectTypeDescription"),
                ProjectCount = r.GetInt32(r.GetOrdinal("ProjectCount"))
            },
            byTypeParams,
            cancellationToken);

        return new HomeSummary
        {
            ActiveProjectCount = activeProjectCount,
            DistinctProjectCount = row.DistinctProjectCount,
            TotalProjectRevenue = totalProjectRevenue,
            OnlineProjectCount = onlineRow.OnlineProjectCount,
            OnlineProjectRevenue = onlineRow.OnlineProjectRevenue,
            ProjectTypeCounts = byType
        };
    }
}
