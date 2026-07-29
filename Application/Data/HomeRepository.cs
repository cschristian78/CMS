using System.Text;
using ClientManagementSystem.Models;
using Microsoft.Data.SqlClient;

namespace ClientManagementSystem.Data;

/// <summary>Home page roll-ups, sourced from <c>dbo.vProject_Current</c> (which surfaces
/// <c>LatestRevenue</c> via an <c>OUTER APPLY</c> on <c>dbo.tProjectRevenue</c>).</summary>
public sealed class HomeRepository : IHomeRepository
{
    private const string ActiveClientProjectPredicate = """
         AND LTRIM(RTRIM(CAST([ClientStatus] AS NVARCHAR(50)))) = N'Active'
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

        var parameters = new List<SqlParameter>();
        ClientProjectListFilterSql.AppendFilters(
            totalsSql,
            parameters,
            businessUnitName,
            statusName,
            clientNameSearch,
            businessClassName,
            projectTypeName,
            isOnline);

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
                CAST(ISNULL((
                    SELECT SUM(CAST(f.LatestRevenue AS float))
                    FROM filtered AS f
                    WHERE f.LatestRevenue IS NOT NULL
                ), 0) AS float) AS TotalProjectRevenue
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

        const string activeClientCountSql = """
            SELECT CAST(COUNT(*) AS int) AS UniqueClientCount
              FROM dbo.tClient AS c
             WHERE c.IsActive = 1
               AND c.ClientStatus = N'Active'
            """;

        var activeClientCount = await _db.QueryTextAsync(
            activeClientCountSql,
            static r => r.GetInt32(r.GetOrdinal("UniqueClientCount")),
            null,
            cancellationToken);

        var uniqueClientCount = activeClientCount.Count > 0 ? activeClientCount[0] : 0;

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
            isOnline);

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
            UniqueClientCount = uniqueClientCount,
            DistinctProjectCount = row.DistinctProjectCount,
            TotalProjectRevenue = row.TotalProjectRevenue,
            ProjectTypeCounts = byType
        };
    }
}
