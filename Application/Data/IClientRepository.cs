using ClientManagementSystem.Models;

namespace ClientManagementSystem.Data;

/// <summary>Reads / writes against the CMS_PhaseI_All schema. The legacy link tables and
/// <c>usp_Ref_CreateClientList</c> are retired — <c>tClient</c> carries direct FKs to
/// <c>BusinessClassID</c> / <c>ClientCategoryID</c> / <c>ClientTypeID</c>, and the directory
/// grid is fed by <c>dbo.vClient_WithBU</c>.</summary>
public interface IClientRepository
{
    /// <summary>Directory grid backed by <c>dbo.vClient_WithBU</c>. Soft-deleted rows are
    /// included only when <paramref name="includeDeleted"/> is true.</summary>
    Task<IReadOnlyList<ClientListRow>> ListAsync(
        string? searchTerm = null,
        int? statusId = null,
        bool includeDeleted = false,
        bool projectEligibleOnly = false,
        CancellationToken cancellationToken = default);

    /// <summary>Single client by id (joined to lookups for display).</summary>
    Task<Client?> GetAsync(int clientId, CancellationToken cancellationToken = default);

    Task<int> AddAsync(Client client, CancellationToken cancellationToken = default);
    Task<int> UpdateAsync(Client client, CancellationToken cancellationToken = default);
    Task<int> SoftDeleteAsync(int clientId, CancellationToken cancellationToken = default);
    Task<int> RestoreAsync(int clientId, CancellationToken cancellationToken = default);

    Task<int> GetProjectCountAsync(int clientId, CancellationToken cancellationToken = default);

    /// <summary>Bulk loader for CSV imports. Each row is inserted via <c>usp_Client_Add</c>
    /// inside a single transaction.</summary>
    Task BulkInsertAsync(IReadOnlyList<Client> clients, CancellationToken cancellationToken = default);
}
