using ClientManagementSystem.Models;
using Microsoft.Data.SqlClient;

namespace ClientManagementSystem.Data;

/// <summary>
/// Backed by <c>dbo.tClient</c> + <c>dbo.tClientRef</c> for reads and the <c>usp_Client_*</c> family for
/// writes. The legacy link tables and <c>usp_Ref_CreateClientList</c>/<c>tClientList</c>
/// path are retired in CMS_PhaseI_All.
/// </summary>
public sealed class ClientRepository : IClientRepository
{
    private const string ClientListSelectSql = """
        SELECT
            c.ClientID,
            c.ClientName,
            c.ClientShortName,
            cr.BusinessUnitID,
            BusinessUnitName = bu.BusinessUnitName,
            cr.BusinessClassID,
            BusinessClassName = bc.BusinessClassName,
            BusinessClassDescription = bc.BusinessClassDescription,
            ClientCategoryID = cr.ClientTypeRefID,
            CCName = ctr.ClientTypeRefName,
            cr.ClientTypeID,
            CTName = ct.ClientTypeName,
            CTDescription = ct.ClientTypeDescription,
            cr.StatusID,
            StatusCode = st.StatusCode,
            StatusDescription = st.StatusDescription,
            c.CreatedDate,
            c.ModifiedDate
          FROM dbo.tClient AS c
          OUTER APPLY (
              SELECT TOP (1)
                  r.BusinessUnitID,
                  r.BusinessClassID,
                  r.ClientTypeID,
                  r.ClientTypeRefID,
                  r.StatusID
                FROM dbo.tClientRef AS r
               WHERE r.ClientID = c.ClientID AND r.IsActive = 1
               ORDER BY r.ClientRefID
          ) AS cr
          LEFT JOIN dbo.tBusinessUnit AS bu ON bu.BusinessUnitID = cr.BusinessUnitID
          LEFT JOIN dbo.tBusinessClass AS bc ON bc.BusinessClassID = cr.BusinessClassID
          LEFT JOIN dbo.tClientTypeRef AS ctr ON ctr.ClientTypeRefID = cr.ClientTypeRefID
          LEFT JOIN dbo.tClientType AS ct ON ct.ClientTypeID = cr.ClientTypeID
          LEFT JOIN dbo.tStatus AS st ON st.StatusID = cr.StatusID
        """;

    private readonly IDbExecutor _db;
    private readonly ISqlConnectionFactory _connections;

    public ClientRepository(IDbExecutor db, ISqlConnectionFactory connections)
    {
        _db = db;
        _connections = connections;
    }

    public async Task<IReadOnlyList<ClientListRow>> ListAsync(
        string? searchTerm = null,
        int? statusId = null,
        bool includeDeleted = false,
        bool projectEligibleOnly = false,
        CancellationToken cancellationToken = default)
    {
        var sql = ClientListSelectSql + """
             WHERE (@IncludeInactive = 1 OR c.IsActive = 1)
               AND (@ProjectEligibleOnly = 0 OR (c.IsActive = 1 AND st.StatusCode IN ('A', 'P')))
               AND (@SearchTerm IS NULL
                    OR c.ClientName LIKE @SearchPattern
                    OR c.ClientShortName LIKE @SearchPattern)
             ORDER BY c.ClientName, c.ClientID;
            """;

        var term = string.IsNullOrWhiteSpace(searchTerm) ? null : searchTerm.Trim();
        var rows = await _db.QueryTextAsync(sql, MapClientListRow,
        [
            SqlParameterFactory.Bit("@IncludeInactive", includeDeleted),
            SqlParameterFactory.Bit("@ProjectEligibleOnly", projectEligibleOnly),
            SqlParameterFactory.NVarChar("@SearchTerm", 150, term),
            SqlParameterFactory.NVarChar("@SearchPattern", 152, term is null ? null : $"%{term}%")
        ], cancellationToken);

        if (statusId is > 0)
        {
            return rows.Where(r => r.StatusID == statusId).ToList();
        }

        return rows;
    }

    public async Task<Client?> GetAsync(int clientId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT
                c.ClientID,
                c.ClientName,
                c.ClientShortName,
                cr.BusinessClassID,
                BusinessClassName = bc.BusinessClassName,
                BusinessClassDescription = bc.BusinessClassDescription,
                cr.BusinessUnitID,
                BusinessUnitName = bu.BusinessUnitName,
                ClientCategoryID = cr.ClientTypeRefID,
                CCName = ctr.ClientTypeRefName,
                cr.ClientTypeID,
                CTName = ct.ClientTypeName,
                CTDescription = ct.ClientTypeDescription,
                cr.StatusID,
                StatusCode = st.StatusCode,
                StatusDescription = st.StatusDescription,
                IsDeleted = CAST(CASE WHEN c.IsActive = 0 THEN 1 ELSE 0 END AS BIT),
                DeletedDate = CAST(NULL AS DATETIME2(0)),
                c.CreatedDate,
                c.ModifiedDate
              FROM dbo.tClient AS c
              OUTER APPLY (
                  SELECT TOP (1)
                      r.BusinessUnitID,
                      r.BusinessClassID,
                      r.ClientTypeID,
                      r.ClientTypeRefID,
                      r.StatusID
                    FROM dbo.tClientRef AS r
                   WHERE r.ClientID = c.ClientID AND r.IsActive = 1
                   ORDER BY r.ClientRefID
              ) AS cr
              LEFT JOIN dbo.tBusinessUnit AS bu ON bu.BusinessUnitID = cr.BusinessUnitID
              LEFT JOIN dbo.tBusinessClass AS bc ON bc.BusinessClassID = cr.BusinessClassID
              LEFT JOIN dbo.tClientTypeRef AS ctr ON ctr.ClientTypeRefID = cr.ClientTypeRefID
              LEFT JOIN dbo.tClientType AS ct ON ct.ClientTypeID = cr.ClientTypeID
              LEFT JOIN dbo.tStatus AS st ON st.StatusID = cr.StatusID
             WHERE c.ClientID = @ClientID;
            """;

        var rows = await _db.QueryTextAsync(sql, MapClient,
        [
            SqlParameterFactory.Int("@ClientID", clientId)
        ], cancellationToken);
        return rows.Count == 0 ? null : rows[0];
    }

    public Task<int> AddAsync(Client client, CancellationToken cancellationToken = default)
    {
        return _db.ExecuteScalarIntAsync("dbo.usp_Client_Add", BuildSaveParameters(client, isUpdate: false), cancellationToken);
    }

    public Task<int> UpdateAsync(Client client, CancellationToken cancellationToken = default)
    {
        return _db.ExecuteScalarIntAsync("dbo.usp_Client_Update", BuildSaveParameters(client, isUpdate: true), cancellationToken);
    }

    public Task<int> SoftDeleteAsync(int clientId, CancellationToken cancellationToken = default)
        => _db.ExecuteScalarIntAsync("dbo.usp_Client_SoftDelete", [SqlParameterFactory.Int("@ClientID", clientId)], cancellationToken);

    public Task<int> RestoreAsync(int clientId, CancellationToken cancellationToken = default)
        => _db.ExecuteScalarIntAsync("dbo.usp_Client_Restore", [SqlParameterFactory.Int("@ClientID", clientId)], cancellationToken);

    public async Task<int> GetProjectCountAsync(int clientId, CancellationToken cancellationToken = default)
    {
        var rows = await _db.QueryAsync("dbo.usp_Client_GetSummary", static r => r.GetInt32OrDefault("ProjectCount"),
        [
            SqlParameterFactory.Int("@ClientID", clientId)
        ], cancellationToken);
        return rows.Count == 0 ? 0 : rows[0];
    }

    public async Task BulkInsertAsync(IReadOnlyList<Client> clients, CancellationToken cancellationToken = default)
    {
        if (clients.Count == 0)
        {
            return;
        }

        await using var connection = await _connections.CreateOpenConnectionAsync(cancellationToken);
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            foreach (var c in clients)
            {
                await using var cmd = connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandType = System.Data.CommandType.StoredProcedure;
                cmd.CommandText = "dbo.usp_Client_Add";
                foreach (var p in BuildSaveParameters(c, isUpdate: false))
                {
                    cmd.Parameters.Add(p);
                }

                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }

            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static SqlParameter[] BuildSaveParameters(Client c, bool isUpdate)
    {
        var parameters = new List<SqlParameter>();
        if (isUpdate)
        {
            parameters.Add(SqlParameterFactory.Int("@ClientID", c.ClientID));
        }

        parameters.AddRange(
        [
            SqlParameterFactory.VarChar("@ClientName", 200, c.ClientName),
            SqlParameterFactory.VarChar("@ClientShortName", 50, c.ClientShortName),
            SqlParameterFactory.Int("@BusinessClassID", c.BusinessClassID),
            SqlParameterFactory.Int("@ClientCategoryID", c.ClientCategoryID),
            SqlParameterFactory.Int("@ClientTypeID", c.ClientTypeID),
            SqlParameterFactory.Int("@StatusID", c.StatusID)
        ]);

        return parameters.ToArray();
    }

    private static Client MapClient(SqlDataReader r) => new()
    {
        ClientID = r.GetInt32OrDefault("ClientID"),
        ClientName = r.GetNullableString("ClientName"),
        ClientShortName = r.GetNullableString("ClientShortName"),
        BusinessClassID = r.GetInt32OrDefault("BusinessClassID"),
        BusinessClassName = r.TryGetNullableString("BusinessClassName"),
        BusinessClassDescription = r.TryGetNullableString("BusinessClassDescription"),
        BusinessUnitID = r.TryGetNullableInt32("BusinessUnitID"),
        BusinessUnitName = r.TryGetNullableString("BusinessUnitName"),
        ClientCategoryID = r.TryGetNullableInt32("ClientCategoryID") ?? 0,
        CCName = r.TryGetNullableString("CCName"),
        ClientTypeID = r.GetInt32OrDefault("ClientTypeID"),
        CTName = r.TryGetNullableString("CTName"),
        CTDescription = r.TryGetNullableString("CTDescription"),
        StatusID = r.GetInt32OrDefault("StatusID"),
        StatusCode = r.TryGetNullableString("StatusCode"),
        StatusDescription = r.TryGetNullableString("StatusDescription"),
        IsDeleted = r.TryGetNullableBoolean("IsDeleted") ?? false,
        DeletedDate = r.TryGetNullableDateTime("DeletedDate"),
        CreatedDate = r.TryGetNullableDateTime("CreatedDate"),
        ModifiedDate = r.TryGetNullableDateTime("ModifiedDate")
    };

    private static ClientListRow MapClientListRow(SqlDataReader r) => new()
    {
        ClientID = r.GetInt32OrDefault("ClientID"),
        ClientName = r.GetNullableString("ClientName"),
        ClientShortName = r.GetNullableString("ClientShortName"),
        BusinessUnitID = r.TryGetNullableInt32("BusinessUnitID"),
        BusinessUnitName = r.TryGetNullableString("BusinessUnitName"),
        BusinessClassID = r.TryGetNullableInt32("BusinessClassID"),
        BusinessClassName = r.TryGetNullableString("BusinessClassName"),
        BusinessClassDescription = r.TryGetNullableString("BusinessClassDescription"),
        ClientCategoryID = r.TryGetNullableInt32("ClientCategoryID"),
        CCName = r.TryGetNullableString("CCName"),
        ClientTypeID = r.TryGetNullableInt32("ClientTypeID"),
        CTName = r.TryGetNullableString("CTName"),
        CTDescription = r.TryGetNullableString("CTDescription"),
        StatusID = r.TryGetNullableInt32("StatusID"),
        StatusCode = r.TryGetNullableString("StatusCode"),
        StatusDescription = r.TryGetNullableString("StatusDescription"),
        CreatedDate = r.TryGetNullableDateTime("CreatedDate"),
        ModifiedDate = r.TryGetNullableDateTime("ModifiedDate")
    };
}
