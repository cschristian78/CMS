using System.Text;
using Microsoft.Data.SqlClient;

namespace ClientManagementSystem.Data;

/// <summary>Shared WHERE fragments for <c>dbo.vProject_Current</c>
/// (BU / BC / status description / online / client / project-type).
/// Client status on Main is <c>StatusDescription</c> from
/// <c>tClientRef.StatusID → tStatus</c> (not <c>tClient.ClientStatus</c> / <c>IsActive</c>).
/// Online status comes from <c>dbo.tProject.IsOnline</c>.</summary>
internal static class ClientProjectListFilterSql
{
    public static void AppendFilters(
        StringBuilder sql,
        IList<SqlParameter> parameters,
        string? businessUnitName,
        string? statusName,
        string? clientNameSearch,
        string? businessClassName,
        string? projectTypeName = null,
        bool? isOnline = null,
        string projectIdColumn = "[ProjectID]",
        IReadOnlyList<string>? analyticsLeadNames = null,
        string analyticsLeadColumn = "[Analytics Lead]")
    {
        if (!string.IsNullOrWhiteSpace(businessUnitName))
        {
            sql.Append(
                " AND LTRIM(RTRIM(CAST([BusinessUnitName] AS NVARCHAR(500)))) = LTRIM(RTRIM(@BusinessUnitName))");
            parameters.Add(SqlParameterFactory.NVarChar("@BusinessUnitName", 500, businessUnitName.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(businessClassName))
        {
            sql.Append(
                " AND LTRIM(RTRIM(CAST([BusinessClassName] AS NVARCHAR(500)))) = LTRIM(RTRIM(@BusinessClassName))");
            parameters.Add(SqlParameterFactory.NVarChar("@BusinessClassName", 500, businessClassName.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(statusName))
        {
            sql.Append(
                " AND LTRIM(RTRIM(CAST([StatusDescription] AS NVARCHAR(500)))) = LTRIM(RTRIM(@StatusDescription))");
            parameters.Add(SqlParameterFactory.NVarChar("@StatusDescription", 500, statusName.Trim()));
        }

        if (isOnline.HasValue)
        {
            // Correlate by ProjectID so this works both on bare vProject_Current and on
            // aliased joins (pass projectIdColumn: "v.ProjectID" when ProjectID is ambiguous).
            sql.Append(
                $"""
                 AND CAST(ISNULL((
                    SELECT TOP (1) pOnline.IsOnline
                    FROM dbo.tProject AS pOnline
                    WHERE pOnline.ProjectID = {projectIdColumn}
                 ), 0) AS BIT) = @IsOnline
                """);
            parameters.Add(SqlParameterFactory.Bit("@IsOnline", isOnline.Value));
        }

        if (!string.IsNullOrWhiteSpace(clientNameSearch))
        {
            sql.Append(
                " AND LTRIM(RTRIM(CAST([ClientName] AS NVARCHAR(500)))) = LTRIM(RTRIM(@ClientName))");
            parameters.Add(SqlParameterFactory.NVarChar("@ClientName", 500, clientNameSearch.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(projectTypeName))
        {
            sql.Append(
                " AND LTRIM(RTRIM(CAST([PTDescription] AS NVARCHAR(500)))) = LTRIM(RTRIM(@PTDescription))");
            parameters.Add(SqlParameterFactory.NVarChar("@PTDescription", 500, projectTypeName.Trim()));
        }

        var leadNames = DistinctNonEmpty(analyticsLeadNames);
        if (leadNames.Count > 0)
        {
            var placeholders = new List<string>(leadNames.Count);
            for (var i = 0; i < leadNames.Count; i++)
            {
                var paramName = $"@AnalyticsLeadName{i}";
                placeholders.Add(paramName);
                parameters.Add(SqlParameterFactory.NVarChar(paramName, 500, leadNames[i]));
            }

            sql.Append(
                $" AND LTRIM(RTRIM(CAST({analyticsLeadColumn} AS NVARCHAR(500)))) IN ({string.Join(", ", placeholders)})");
        }
    }

    private static IReadOnlyList<string> DistinctNonEmpty(IReadOnlyList<string>? values)
    {
        if (values is null || values.Count == 0)
        {
            return [];
        }

        return values
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
