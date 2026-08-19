using ClientManagementSystem.Models;
using ClientManagementSystem.ViewModels;

namespace ClientManagementSystem.Services;

public interface IHomeService
{
    Task<HomeSummary> GetHomeSummaryAsync(
        string? businessUnitName = null,
        string? statusName = null,
        string? clientNameSearch = null,
        string? businessClassName = null,
        string? projectTypeName = null,
        bool? isOnline = null,
        IReadOnlyList<string>? analyticsLeadNames = null,
        CancellationToken cancellationToken = default);

    Task<ClientProjectListViewModel> GetHomeClientProjectListAsync(
        bool refresh,
        string? businessUnitName,
        string? statusName,
        string? clientNameSearch,
        string? businessClassName,
        string? projectTypeName = null,
        bool? isOnline = null,
        IReadOnlyList<string>? analyticsLeadNames = null,
        CancellationToken cancellationToken = default);

    Task<ClientProjectListFilterOptionsViewModel> GetHomeClientProjectListFilterOptionsAsync(
        CancellationToken cancellationToken = default);

    Task<HomeClientProjectBulkParseResult> ParseClientProjectImportCsvAsync(Stream csvStream, CancellationToken cancellationToken = default);

    Task<HomeClientProjectBulkVerifyResult> VerifyClientProjectImportRowsAsync(
        IReadOnlyList<HomeClientProjectImportRowDto> rows,
        CancellationToken cancellationToken = default);

    Task<HomeClientProjectBulkImportResult> CommitClientProjectImportRowsAsync(
        IReadOnlyList<HomeClientProjectImportRowDto> rows,
        CancellationToken cancellationToken = default);
}
