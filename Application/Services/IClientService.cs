using ClientManagementSystem.Models;
using ClientManagementSystem.ViewModels;

namespace ClientManagementSystem.Services;

public interface IClientService
{
    Task<ClientFormViewModel> PrepareCreateFormAsync(ClientFormViewModel? model = null, CancellationToken cancellationToken = default);
    Task<ClientFormViewModel?> PrepareEditFormAsync(int clientId, CancellationToken cancellationToken = default);

    /// <summary>Business classes from <c>dbo.tBusinessClass</c> where <c>BusinessUnitID</c>
    /// matches (client form cascading dropdown).</summary>
    Task<IReadOnlyList<(string Value, string Text)>> GetBusinessClassOptionsForBusinessUnitAsync(
        int businessUnitId,
        CancellationToken cancellationToken = default);

    Task<int> SaveAsync(ClientFormViewModel model, CancellationToken cancellationToken = default);
    Task SoftDeleteAsync(int clientId, CancellationToken cancellationToken = default);
    Task RestoreAsync(int clientId, CancellationToken cancellationToken = default);

    /// <summary>Directory grid driven by <c>dbo.vClient_WithBU</c> via <c>usp_Client_List</c>.</summary>
    Task<IReadOnlyList<ClientListRow>> ListAsync(string? searchTerm = null, int? statusId = null, bool includeDeleted = false, CancellationToken cancellationToken = default);

    /// <summary>Single row from <c>dbo.vClient_WithBU</c> for the Project form Client tab.</summary>
    Task<ClientListRow?> GetClientListRowAsync(int clientId, CancellationToken cancellationToken = default);

    Task<ClientBulkParseResult> ParseImportCsvAsync(Stream csvStream, CancellationToken cancellationToken = default);
    Task<ClientBulkVerifyResult> VerifyImportRowsAsync(IReadOnlyList<ClientImportRowDto> rows, CancellationToken cancellationToken = default);
    Task<ClientBulkImportResult> CommitImportRowsAsync(IReadOnlyList<ClientImportRowDto> rows, CancellationToken cancellationToken = default);
}
