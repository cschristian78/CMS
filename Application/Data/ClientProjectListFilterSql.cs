using System.Text;
using Microsoft.Data.SqlClient;

namespace ClientManagementSystem.Data;

/// <summary>Shared WHERE fragments for <c>dbo.vProject_Current</c>
/// (BU / BC / client-status / client / project-type).
/// The legacy <c>dbo.tClientProjectList</c> table is retired in CMS_PhaseI_All; filtering is
/// now driven by the projection of <c>vProject_Current</c> which exposes <c>BUName</c>,
/// <c>BCName</c>, <c>ClientStatus</c> (alias for the client&apos;s <c>StatusDescription</c>),
/// <c>ClientName</c>, and <c>PTDescription</c>.</summary>
internal static class ClientProjectListFilterSql
{
    public static void AppendFilters(
        StringBuilder sql,
        IList<SqlParameter> parameters,
        string? businessUnitName,
        string? statusName,
        string? clientNameSearch,
        string? businessClassName,
        string? projectTypeName = null)
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
                " AND LTRIM(RTRIM(CAST([ClientStatus] AS NVARCHAR(500)))) = LTRIM(RTRIM(@ClientStatus))");
            parameters.Add(SqlParameterFactory.NVarChar("@ClientStatus", 500, statusName.Trim()));
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
    }
}
