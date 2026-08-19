using ClientManagementSystem.Models;
using Microsoft.Data.SqlClient;

namespace ClientManagementSystem.Data;

/// <summary>
/// Backed by <c>dbo.tClient</c> + <c>dbo.tClientRef</c> for reads and the <c>usp_Client_*</c> family for
/// writes. Client Active/Inactive/Prospect for UI and Main is <c>tClientRef.StatusID → tStatus</c>.
/// </summary>
public sealed class ClientRepository : IClientRepository
{
    /// <summary>
    /// Status comes from the primary active ClientRef → tStatus (not tClient.ClientStatus / IsActive).
    /// </summary>
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
            StatusID = cr.StatusID,
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
               ORDER BY r.ModifiedDate DESC, r.ClientRefID DESC
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
             WHERE (@IncludeInactive = 1 OR st.StatusDescription IS NULL OR st.StatusDescription <> N'Inactive')
               AND (@ProjectEligibleOnly = 0 OR st.StatusDescription IN (N'Active', N'Prospect'))
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
                StatusID = cr.StatusID,
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
                   ORDER BY r.ModifiedDate DESC, r.ClientRefID DESC
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

    public async Task<int> AddAsync(Client client, CancellationToken cancellationToken = default)
    {
        var status = ResolveClientStatus(client);
        var statusId = StatusIdFromClientStatus(status);
        var output = SqlParameterFactory.OutputInt("@NewClientID");
        await _db.ExecuteWithOutputIntAsync(
            "dbo.usp_Client_Create",
            [
                SqlParameterFactory.Int("@AccountExecutiveUserID", null),
                SqlParameterFactory.Int("@AnalyticsLeadUserID", null),
                SqlParameterFactory.VarChar("@ClientName", 150, client.ClientName),
                SqlParameterFactory.VarChar("@ClientDescription", 500, null),
                SqlParameterFactory.VarChar("@ClientShortName", 50, client.ClientShortName),
                SqlParameterFactory.VarChar("@ClientStatus", 10, status),
                SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId),
                output
            ],
            "@NewClientID",
            cancellationToken);

        var clientId = Convert.ToInt32(output.Value);
        await SyncClientClassificationAndStatusAsync(clientId, client, statusId, cancellationToken);
        return clientId;
    }

    public async Task<int> UpdateAsync(Client client, CancellationToken cancellationToken = default)
    {
        var status = ResolveClientStatus(client);
        var statusId = StatusIdFromClientStatus(status);

        await _db.ExecuteAsync(
            "dbo.usp_Client_Update",
            [
                SqlParameterFactory.Int("@ClientID", client.ClientID),
                SqlParameterFactory.Int("@AccountExecutiveUserID", null),
                SqlParameterFactory.Int("@AnalyticsLeadUserID", null),
                SqlParameterFactory.VarChar("@ClientName", 150, client.ClientName),
                SqlParameterFactory.VarChar("@ClientDescription", 500, null),
                SqlParameterFactory.VarChar("@ClientShortName", 50, client.ClientShortName),
                SqlParameterFactory.VarChar("@ClientStatus", 10, status),
                SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId)
            ],
            cancellationToken);

        await SyncClientClassificationAndStatusAsync(client.ClientID, client, statusId, cancellationToken);
        return client.ClientID;
    }

    public async Task<int> SoftDeleteAsync(int clientId, CancellationToken cancellationToken = default)
    {
        await _db.ExecuteAsync(
            "dbo.usp_Client_SoftDelete",
            [
                SqlParameterFactory.Int("@ClientID", clientId),
                SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId)
            ],
            cancellationToken);

        await SyncAllClientRefStatusAsync(clientId, StatusIdFromClientStatus("Inactive"), cancellationToken);
        return clientId;
    }

    public async Task<int> RestoreAsync(int clientId, CancellationToken cancellationToken = default)
    {
        // No dedicated restore proc in CMS_PhaseI_All_v3 — reactivate via ClientStatus = Active.
        await _db.ExecuteAsync(
            "dbo.usp_Client_Update",
            [
                SqlParameterFactory.Int("@ClientID", clientId),
                SqlParameterFactory.Int("@AccountExecutiveUserID", null),
                SqlParameterFactory.Int("@AnalyticsLeadUserID", null),
                SqlParameterFactory.VarChar("@ClientName", 150, null),
                SqlParameterFactory.VarChar("@ClientDescription", 500, null),
                SqlParameterFactory.VarChar("@ClientShortName", 50, null),
                SqlParameterFactory.VarChar("@ClientStatus", 10, "Active"),
                SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId)
            ],
            cancellationToken);

        await SyncAllClientRefStatusAsync(clientId, StatusIdFromClientStatus("Active"), cancellationToken);
        return clientId;
    }

    public async Task<int> GetProjectCountAsync(int clientId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT CAST(COUNT(DISTINCT p.ProjectID) AS int) AS ProjectCount
              FROM dbo.tProject AS p
              INNER JOIN dbo.tProjectRef AS pr ON pr.ProjectID = p.ProjectID AND pr.IsActive = 1
              INNER JOIN dbo.tClientRef AS cr ON cr.ClientRefID = pr.ClientRefID AND cr.IsActive = 1
             WHERE cr.ClientID = @ClientID
               AND p.IsActive = 1;
            """;

        var rows = await _db.QueryTextAsync(sql, static r => r.GetInt32(r.GetOrdinal("ProjectCount")),
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

        foreach (var c in clients)
        {
            await AddAsync(c, cancellationToken);
        }
    }

    /// <summary>
    /// Persist BU / BC / type on the ClientRef for that business unit (creating a new
    /// ref when the client already has other BU rows) and force every active ref's
    /// StatusID to match the client's overall ClientStatus.
    /// </summary>
    private async Task SyncClientClassificationAndStatusAsync(
        int clientId,
        Client client,
        int statusId,
        CancellationToken cancellationToken)
    {
        var businessUnitId = client.BusinessUnitID > 0 ? client.BusinessUnitID : null;
        var businessClassId = client.BusinessClassID > 0 ? client.BusinessClassID : (int?)null;
        var clientTypeId = client.ClientTypeID > 0 ? client.ClientTypeID : (int?)null;
        var clientCategoryId = client.ClientCategoryID > 0 ? client.ClientCategoryID : (int?)null;

        var existingRefId = await ResolveClientRefIdForClassificationAsync(clientId, businessUnitId, cancellationToken);

        if (businessUnitId is null && businessClassId is null && clientTypeId is null && clientCategoryId is null && existingRefId is null)
        {
            return;
        }

        try
        {
            if (existingRefId is null)
            {
                var output = SqlParameterFactory.OutputInt("@NewClientRefID");
                await _db.ExecuteWithOutputIntAsync(
                    "dbo.usp_ClientRef_Create",
                    [
                        SqlParameterFactory.Int("@ClientID", clientId),
                        SqlParameterFactory.Int("@BusinessUnitID", businessUnitId),
                        SqlParameterFactory.Int("@BusinessClassID", businessClassId),
                        SqlParameterFactory.Int("@ClientTypeID", clientTypeId),
                        SqlParameterFactory.Int("@ClientTypeRefID", clientCategoryId),
                        SqlParameterFactory.Int("@StatusID", statusId),
                        SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId),
                        output
                    ],
                    "@NewClientRefID",
                    cancellationToken);
            }
            else
            {
                await _db.ExecuteAsync(
                    "dbo.usp_ClientRef_Update",
                    [
                        SqlParameterFactory.Int("@ClientRefID", existingRefId.Value),
                        SqlParameterFactory.Int("@ClientID", clientId),
                        SqlParameterFactory.Int("@BusinessUnitID", businessUnitId),
                        SqlParameterFactory.Int("@BusinessClassID", businessClassId),
                        SqlParameterFactory.Int("@ClientTypeID", clientTypeId),
                        SqlParameterFactory.Int("@ClientTypeRefID", clientCategoryId),
                        SqlParameterFactory.Int("@StatusID", statusId),
                        SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId)
                    ],
                    cancellationToken);
            }
        }
        catch (SqlException ex) when (IsDuplicateClientBusinessUnit(ex))
        {
            throw DuplicateClientBusinessUnitException(ex);
        }

        await SyncAllClientRefStatusAsync(clientId, statusId, cancellationToken);
    }

    private Task SyncAllClientRefStatusAsync(int clientId, int statusId, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE dbo.tClientRef
               SET StatusID = @StatusID,
                   ModifiedDate = SYSUTCDATETIME()
             WHERE ClientID = @ClientID
               AND IsActive = 1
               AND StatusID <> @StatusID;
            """;

        return _db.ExecuteTextAsync(sql,
        [
            SqlParameterFactory.Int("@ClientID", clientId),
            SqlParameterFactory.Int("@StatusID", statusId)
        ], cancellationToken);
    }

    private async Task<int?> GetPrimaryClientRefIdAsync(int clientId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT TOP (1) ClientRefID
              FROM dbo.tClientRef
             WHERE ClientID = @ClientID
               AND IsActive = 1
             ORDER BY ClientRefID;
            """;

        var rows = await _db.QueryTextAsync(sql, static r => r.GetInt32(0),
        [
            SqlParameterFactory.Int("@ClientID", clientId)
        ], cancellationToken);

        return rows.Count == 0 ? null : rows[0];
    }

    /// <summary>
    /// Unique index <c>UX_tClientRef_Client_BU_Active</c> allows one active ref per
    /// (ClientID, BusinessUnitID). Update the matching BU row, or insert a new one when
    /// this client already has other BU rows (projects may still point at those).
    /// </summary>
    private async Task<int?> ResolveClientRefIdForClassificationAsync(
        int clientId,
        int? businessUnitId,
        CancellationToken cancellationToken)
    {
        if (businessUnitId is > 0)
        {
            var matching = await GetClientRefIdForBusinessUnitAsync(clientId, businessUnitId.Value, cancellationToken);
            if (matching is > 0)
            {
                return matching;
            }

            var activeCount = await CountActiveClientRefsAsync(clientId, cancellationToken);
            if (activeCount > 1)
            {
                return null;
            }
        }

        return await GetPrimaryClientRefIdAsync(clientId, cancellationToken);
    }

    private async Task<int?> GetClientRefIdForBusinessUnitAsync(
        int clientId,
        int businessUnitId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT TOP (1) ClientRefID
              FROM dbo.tClientRef
             WHERE ClientID = @ClientID
               AND BusinessUnitID = @BusinessUnitID
               AND IsActive = 1
             ORDER BY ClientRefID;
            """;

        var rows = await _db.QueryTextAsync(sql, static r => r.GetInt32(0),
        [
            SqlParameterFactory.Int("@ClientID", clientId),
            SqlParameterFactory.Int("@BusinessUnitID", businessUnitId)
        ], cancellationToken);

        return rows.Count == 0 ? null : rows[0];
    }

    private async Task<int> CountActiveClientRefsAsync(int clientId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT CAST(COUNT(*) AS int) AS RefCount
              FROM dbo.tClientRef
             WHERE ClientID = @ClientID
               AND IsActive = 1;
            """;

        var rows = await _db.QueryTextAsync(sql, static r => r.GetInt32(0),
        [
            SqlParameterFactory.Int("@ClientID", clientId)
        ], cancellationToken);
        return rows.Count == 0 ? 0 : rows[0];
    }

    private static bool IsDuplicateClientBusinessUnit(SqlException ex)
        => ex.Number is 2601 or 2627
           && ex.Message.Contains("UX_tClientRef_Client_BU_Active", StringComparison.OrdinalIgnoreCase);

    private static InvalidOperationException DuplicateClientBusinessUnitException(SqlException ex)
        => new(
            "This client already has an active classification for that business unit. Each client can have only one active business unit (US, UK, LA, or Global) row.",
            ex);

    /// <summary>Map UI StatusID / description to tClient.ClientStatus values.</summary>
    private static string ResolveClientStatus(Client client)
    {
        var description = client.StatusDescription?.Trim();
        if (string.Equals(description, "Inactive", StringComparison.OrdinalIgnoreCase))
        {
            return "Inactive";
        }

        if (string.Equals(description, "Prospect", StringComparison.OrdinalIgnoreCase))
        {
            return "Prospect";
        }

        if (string.Equals(description, "Active", StringComparison.OrdinalIgnoreCase))
        {
            return "Active";
        }

        return StatusIdFromClientStatusInverse(client.StatusID);
    }

    private static string StatusIdFromClientStatusInverse(int statusId) => statusId switch
    {
        2 => "Inactive",
        3 => "Prospect",
        _ => "Active"
    };

    /// <summary>Seeded tStatus ids: 1=Active, 2=Inactive, 3=Prospect.</summary>
    private static int StatusIdFromClientStatus(string status) => status switch
    {
        "Inactive" => 2,
        "Prospect" => 3,
        _ => 1
    };

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
