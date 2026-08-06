using System.Text;
using ClientManagementSystem.Models;
using Microsoft.Data.SqlClient;

namespace ClientManagementSystem.Data;

/// <summary>Dashboard analytics and <c>dbo.vProject_Current</c> list access.</summary>
public sealed class DashboardRepository : IDashboardRepository
{
    /// <summary>Role-pivoted column on <c>dbo.vProject_Current</c> / <c>vProjectList</c>.</summary>
    private const string AnalyticsLeadColumn = "Analytics Lead";

    private readonly IDbExecutor _db;

    public DashboardRepository(IDbExecutor db)
    {
        _db = db;
    }

    public Task<IReadOnlyList<string>> GetDistinctAnalyticsLeadNamesAsync(
        CancellationToken cancellationToken = default)
    {
        // Bracketed identifier — role column name includes a space.
        var sql = $"""
            SELECT DISTINCT LTRIM(RTRIM(CAST(v.[{AnalyticsLeadColumn}] AS nvarchar(500)))) AS Value
            FROM dbo.vProject_Current AS v
            WHERE v.[{AnalyticsLeadColumn}] IS NOT NULL
              AND LTRIM(RTRIM(CAST(v.[{AnalyticsLeadColumn}] AS nvarchar(500)))) <> N''
            ORDER BY Value
            """;

        return _db.QueryTextAsync(
            sql,
            static r => r.GetNullableString("Value")?.Trim() ?? string.Empty,
            null,
            cancellationToken);
    }

    public Task<IReadOnlyList<DashboardBusinessUnitCount>> GetClientCountByBusinessUnitAsync(
        string? analyticsLeadName = null,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(analyticsLeadName))
        {
            var filteredSql = $"""
                SELECT
                    CAST(bu.BusinessUnitID AS int) AS BusinessUnitID,
                    CAST(bu.BusinessUnitName AS nvarchar(255)) AS BusinessUnitName,
                    CAST(COUNT(DISTINCT v.ClientID) AS int) AS ClientCount
                FROM dbo.tBusinessUnit AS bu
                LEFT JOIN dbo.vProject_Current AS v
                  ON v.BusinessUnitName = bu.BusinessUnitName
                 AND v.ClientID IS NOT NULL
                 AND LTRIM(RTRIM(CAST(v.[{AnalyticsLeadColumn}] AS nvarchar(500))))
                     = LTRIM(RTRIM(@AnalyticsLeadName))
                WHERE bu.IsActive = 1
                GROUP BY bu.BusinessUnitID, bu.BusinessUnitName
                ORDER BY bu.BusinessUnitName;
                """;

            return _db.QueryTextAsync(
                filteredSql,
                MapBusinessUnitCount,
                [SqlParameterFactory.NVarChar("@AnalyticsLeadName", 500, analyticsLeadName.Trim())],
                cancellationToken);
        }

        const string sql = """
            SELECT
                CAST(bu.BusinessUnitID AS int) AS BusinessUnitID,
                CAST(bu.BusinessUnitName AS nvarchar(255)) AS BusinessUnitName,
                CAST(COUNT(DISTINCT cb.ClientID) AS int) AS ClientCount
            FROM dbo.tBusinessUnit AS bu
            LEFT JOIN (
                SELECT
                    c.ClientID,
                    cr.BusinessUnitID
                FROM dbo.tClient AS c
                OUTER APPLY (
                    SELECT TOP (1)
                        r.BusinessUnitID
                    FROM dbo.tClientRef AS r
                    WHERE r.ClientID = c.ClientID
                      AND r.IsActive = 1
                    ORDER BY r.ClientRefID
                ) AS cr
                WHERE c.IsActive = 1
                  AND cr.BusinessUnitID IS NOT NULL
            ) AS cb ON cb.BusinessUnitID = bu.BusinessUnitID
            WHERE bu.IsActive = 1
            GROUP BY bu.BusinessUnitID, bu.BusinessUnitName
            ORDER BY bu.BusinessUnitName;
            """;

        return _db.QueryTextAsync(sql, MapBusinessUnitCount, null, cancellationToken);
    }

    public Task<IReadOnlyList<DashboardBusinessUnitCount>> GetProjectCountByBusinessUnitAsync(
        string? analyticsLeadName = null,
        CancellationToken cancellationToken = default)
    {
        var leadFilter = string.IsNullOrWhiteSpace(analyticsLeadName)
            ? string.Empty
            : $"""
               AND LTRIM(RTRIM(CAST(v.[{AnalyticsLeadColumn}] AS nvarchar(500))))
                   = LTRIM(RTRIM(@AnalyticsLeadName))
              """;

        var sql = $"""
            SELECT
                CAST(bu.BusinessUnitID AS int) AS BusinessUnitID,
                CAST(bu.BusinessUnitName AS nvarchar(255)) AS BusinessUnitName,
                CAST(COUNT(DISTINCT v.ProjectID) AS int) AS ClientCount
            FROM dbo.tBusinessUnit AS bu
            LEFT JOIN dbo.vProject_Current AS v
              ON v.BusinessUnitName = bu.BusinessUnitName
             AND v.ProjectID IS NOT NULL
             {leadFilter}
            WHERE bu.IsActive = 1
            GROUP BY bu.BusinessUnitID, bu.BusinessUnitName
            ORDER BY bu.BusinessUnitName;
            """;

        var parameters = string.IsNullOrWhiteSpace(analyticsLeadName)
            ? null
            : new List<SqlParameter>
            {
                SqlParameterFactory.NVarChar("@AnalyticsLeadName", 500, analyticsLeadName.Trim())
            };

        return _db.QueryTextAsync(sql, MapBusinessUnitCount, parameters, cancellationToken);
    }

    public Task<IReadOnlyList<DashboardNamedCount>> GetClientCountByBusinessClassAsync(
        string? analyticsLeadName = null,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(analyticsLeadName))
        {
            var filteredSql = $"""
                ;WITH base AS (
                    SELECT
                        BusinessClassName = COALESCE(
                            NULLIF(LTRIM(RTRIM(CAST(v.BusinessClassName AS NVARCHAR(255)))), N''),
                            N'(Unspecified)'),
                        BusinessUnitName = COALESCE(
                            NULLIF(LTRIM(RTRIM(CAST(v.BusinessUnitName AS NVARCHAR(100)))), N''),
                            N'(Unassigned)'),
                        v.ClientID
                    FROM dbo.vProject_Current AS v
                    WHERE v.ClientID IS NOT NULL
                      AND LTRIM(RTRIM(CAST(v.[{AnalyticsLeadColumn}] AS nvarchar(500))))
                          = LTRIM(RTRIM(@AnalyticsLeadName))
                ),
                byUnit AS (
                    SELECT
                        BusinessClassName,
                        BusinessUnitName,
                        CAST(COUNT(DISTINCT ClientID) AS int) AS UnitCount
                    FROM base
                    GROUP BY BusinessClassName, BusinessUnitName
                ),
                ranked AS (
                    SELECT
                        BusinessClassName,
                        BusinessUnitName,
                        UnitCount,
                        ROW_NUMBER() OVER (
                            PARTITION BY BusinessClassName
                            ORDER BY UnitCount DESC, BusinessUnitName) AS rn
                    FROM byUnit
                ),
                totals AS (
                    SELECT
                        BusinessClassName,
                        CAST(COUNT(DISTINCT ClientID) AS int) AS Count
                    FROM base
                    GROUP BY BusinessClassName
                ),
                allClasses AS (
                    SELECT
                        Label = COALESCE(
                            NULLIF(LTRIM(RTRIM(bc.BusinessClassDescription)), N''),
                            bc.BusinessClassName)
                    FROM dbo.tBusinessClass AS bc
                    WHERE bc.IsActive = 1
                )
                SELECT
                    a.Label,
                    CAST(ISNULL(t.Count, 0) AS int) AS Count,
                    r.BusinessUnitName AS ColorKey
                FROM allClasses AS a
                LEFT JOIN totals AS t ON t.BusinessClassName = a.Label
                LEFT JOIN ranked AS r ON r.BusinessClassName = a.Label AND r.rn = 1
                ORDER BY a.Label;
                """;

            return _db.QueryTextAsync(
                filteredSql,
                MapNamedCount,
                [SqlParameterFactory.NVarChar("@AnalyticsLeadName", 500, analyticsLeadName.Trim())],
                cancellationToken);
        }

        const string sql = """
            ;WITH base AS (
                SELECT
                    bc.BusinessClassID,
                    BusinessClassName = COALESCE(
                        NULLIF(LTRIM(RTRIM(bc.BusinessClassDescription)), N''),
                        bc.BusinessClassName),
                    BusinessUnitName = COALESCE(
                        NULLIF(LTRIM(RTRIM(bu.BusinessUnitName)), N''),
                        N'(Unassigned)'),
                    c.ClientID
                FROM dbo.tClient AS c
                OUTER APPLY (
                    SELECT TOP (1)
                        r.BusinessClassID,
                        r.BusinessUnitID
                    FROM dbo.tClientRef AS r
                    WHERE r.ClientID = c.ClientID
                      AND r.IsActive = 1
                    ORDER BY r.ClientRefID
                ) AS cr
                LEFT JOIN dbo.tBusinessClass AS bc ON bc.BusinessClassID = cr.BusinessClassID
                LEFT JOIN dbo.tBusinessUnit AS bu ON bu.BusinessUnitID = cr.BusinessUnitID
                WHERE c.IsActive = 1
            ),
            byUnit AS (
                SELECT
                    BusinessClassID,
                    BusinessUnitName,
                    CAST(COUNT(DISTINCT ClientID) AS int) AS UnitCount
                FROM base
                WHERE BusinessClassID IS NOT NULL
                GROUP BY BusinessClassID, BusinessUnitName
            ),
            ranked AS (
                SELECT
                    BusinessClassID,
                    BusinessUnitName,
                    UnitCount,
                    ROW_NUMBER() OVER (
                        PARTITION BY BusinessClassID
                        ORDER BY UnitCount DESC, BusinessUnitName) AS rn
                FROM byUnit
            ),
            totals AS (
                SELECT
                    BusinessClassID,
                    CAST(COUNT(DISTINCT ClientID) AS int) AS Count
                FROM base
                WHERE BusinessClassID IS NOT NULL
                GROUP BY BusinessClassID
            ),
            allClasses AS (
                SELECT
                    bc.BusinessClassID,
                    Label = COALESCE(
                        NULLIF(LTRIM(RTRIM(bc.BusinessClassDescription)), N''),
                        bc.BusinessClassName)
                FROM dbo.tBusinessClass AS bc
                WHERE bc.IsActive = 1
            )
            SELECT
                a.Label,
                CAST(ISNULL(t.Count, 0) AS int) AS Count,
                r.BusinessUnitName AS ColorKey
            FROM allClasses AS a
            LEFT JOIN totals AS t ON t.BusinessClassID = a.BusinessClassID
            LEFT JOIN ranked AS r ON r.BusinessClassID = a.BusinessClassID AND r.rn = 1
            ORDER BY a.Label;
            """;

        return _db.QueryTextAsync(sql, MapNamedCount, null, cancellationToken);
    }

    public Task<IReadOnlyList<DashboardNamedCount>> GetProjectCountByBusinessClassAsync(
        string? analyticsLeadName = null,
        CancellationToken cancellationToken = default)
    {
        var leadFilter = string.IsNullOrWhiteSpace(analyticsLeadName)
            ? string.Empty
            : $"""
               AND LTRIM(RTRIM(CAST(v.[{AnalyticsLeadColumn}] AS nvarchar(500))))
                   = LTRIM(RTRIM(@AnalyticsLeadName))
              """;

        var sql = $"""
            ;WITH base AS (
                SELECT
                    BusinessClassName = COALESCE(
                        NULLIF(LTRIM(RTRIM(CAST(v.BusinessClassName AS NVARCHAR(255)))), N''),
                        N'(Unspecified)'),
                    BusinessUnitName = COALESCE(
                        NULLIF(LTRIM(RTRIM(CAST(v.BusinessUnitName AS NVARCHAR(100)))), N''),
                        N'(Unassigned)'),
                    v.ProjectID
                FROM dbo.vProject_Current AS v
                WHERE v.ProjectID IS NOT NULL
                  {leadFilter}
            ),
            byUnit AS (
                SELECT
                    BusinessClassName,
                    BusinessUnitName,
                    CAST(COUNT(DISTINCT ProjectID) AS int) AS UnitCount
                FROM base
                GROUP BY BusinessClassName, BusinessUnitName
            ),
            ranked AS (
                SELECT
                    BusinessClassName,
                    BusinessUnitName,
                    UnitCount,
                    ROW_NUMBER() OVER (
                        PARTITION BY BusinessClassName
                        ORDER BY UnitCount DESC, BusinessUnitName) AS rn
                FROM byUnit
            ),
            totals AS (
                SELECT
                    BusinessClassName,
                    CAST(COUNT(DISTINCT ProjectID) AS int) AS Count
                FROM base
                GROUP BY BusinessClassName
            ),
            allClasses AS (
                SELECT
                    Label = COALESCE(
                        NULLIF(LTRIM(RTRIM(bc.BusinessClassDescription)), N''),
                        bc.BusinessClassName)
                FROM dbo.tBusinessClass AS bc
                WHERE bc.IsActive = 1
            )
            SELECT
                a.Label,
                CAST(ISNULL(t.Count, 0) AS int) AS Count,
                r.BusinessUnitName AS ColorKey
            FROM allClasses AS a
            LEFT JOIN totals AS t ON t.BusinessClassName = a.Label
            LEFT JOIN ranked AS r ON r.BusinessClassName = a.Label AND r.rn = 1
            ORDER BY a.Label;
            """;

        var parameters = string.IsNullOrWhiteSpace(analyticsLeadName)
            ? null
            : new List<SqlParameter>
            {
                SqlParameterFactory.NVarChar("@AnalyticsLeadName", 500, analyticsLeadName.Trim())
            };

        return _db.QueryTextAsync(sql, MapNamedCount, parameters, cancellationToken);
    }

    public Task RefreshClientProjectListAsync(CancellationToken cancellationToken = default) =>
        _db.ExecuteAsync("dbo.usp_Create_vProject_Current", null, cancellationToken);

    public Task<IReadOnlyList<string>> GetClientProjectListDistinctColumnValuesAsync(
        string columnName,
        CancellationToken cancellationToken = default)
    {
        if (!IsClientProjectListFilterColumn(columnName))
        {
            throw new ArgumentOutOfRangeException(nameof(columnName));
        }

        var sql = $"""
            SELECT DISTINCT [{columnName}] AS Value
            FROM dbo.vProject_Current
            WHERE [{columnName}] IS NOT NULL
              AND LTRIM(RTRIM(CAST([{columnName}] AS nvarchar(500)))) <> N''
            ORDER BY [{columnName}]
            """;

        return _db.QueryTextAsync(
            sql,
            static r => r.GetNullableString("Value")?.Trim() ?? string.Empty,
            null,
            cancellationToken);
    }

    public Task<IReadOnlyList<string>> GetDistinctClientNamesForFiltersAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT DISTINCT LTRIM(RTRIM(c.ClientName)) AS ClientName
            FROM dbo.vClient AS c
            WHERE c.ClientName IS NOT NULL
              AND LTRIM(RTRIM(c.ClientName)) <> N''
            ORDER BY ClientName;
            """;

        return _db.QueryTextAsync(
            sql,
            static r => r.GetNullableString("ClientName")?.Trim() ?? string.Empty,
            null,
            cancellationToken);
    }

    public Task<RawSqlTableResult> GetClientProjectListTableAsync(
        string? businessUnitName,
        string? statusName,
        string? clientNameSearch,
        string? businessClassName,
        string? projectTypeName = null,
        bool? isOnline = null,
        CancellationToken cancellationToken = default)
    {
        var sql = new StringBuilder("""
            SELECT
                CAST(ISNULL(tp.IsOnline, 0) AS BIT) AS ProjectOnline,
                v.*
            FROM dbo.vProject_Current AS v
            LEFT JOIN dbo.tProject AS tp ON tp.ProjectID = v.ProjectID
            WHERE 1 = 1
            """);
        var parameters = new List<SqlParameter>();
        ClientProjectListFilterSql.AppendFilters(
            sql,
            parameters,
            businessUnitName,
            statusName,
            clientNameSearch,
            businessClassName,
            projectTypeName,
            isOnline,
            projectIdColumn: "v.ProjectID");

        return _db.QueryTextAsTableAsync(sql.ToString(), parameters, cancellationToken);
    }

    private static DashboardBusinessUnitCount MapBusinessUnitCount(SqlDataReader r) =>
        new()
        {
            BusinessUnitID = r.GetInt32OrDefault("BusinessUnitID"),
            BusinessUnitName = r.GetNullableString("BusinessUnitName"),
            ClientCount = r.GetInt32OrDefault("ClientCount")
        };

    private static DashboardNamedCount MapNamedCount(SqlDataReader r) =>
        new()
        {
            Label = r.GetNullableString("Label"),
            Count = r.GetInt32OrDefault("Count"),
            ColorKey = r.GetNullableString("ColorKey")
        };

    private static bool IsClientProjectListFilterColumn(string columnName) =>
        string.Equals(columnName, "BusinessUnitName", StringComparison.OrdinalIgnoreCase)
        || string.Equals(columnName, "BusinessClassName", StringComparison.OrdinalIgnoreCase)
        || string.Equals(columnName, "StatusDescription", StringComparison.OrdinalIgnoreCase)
        || string.Equals(columnName, "ProjectStatus", StringComparison.OrdinalIgnoreCase)
        || string.Equals(columnName, "ClientName", StringComparison.OrdinalIgnoreCase)
        || string.Equals(columnName, "PTDescription", StringComparison.OrdinalIgnoreCase);
}
