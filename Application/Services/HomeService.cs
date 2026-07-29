using ClientManagementSystem.Data;
using ClientManagementSystem.Models;
using ClientManagementSystem.ViewModels;

namespace ClientManagementSystem.Services;

public sealed class HomeService : IHomeService
{
    private readonly IHomeRepository _home;
    private readonly IClientProjectListService _clientProjectList;
    private readonly HomeClientProjectBulkImportService _bulkImport;

    public HomeService(
        IHomeRepository home,
        IClientProjectListService clientProjectList,
        HomeClientProjectBulkImportService bulkImport)
    {
        _home = home;
        _clientProjectList = clientProjectList;
        _bulkImport = bulkImport;
    }

    public Task<HomeSummary> GetHomeSummaryAsync(
        string? businessUnitName = null,
        string? statusName = null,
        string? clientNameSearch = null,
        string? businessClassName = null,
        string? projectTypeName = null,
        bool? isOnline = null,
        CancellationToken cancellationToken = default) =>
        _home.GetHomeSummaryAsync(
            businessUnitName,
            statusName,
            clientNameSearch,
            businessClassName,
            projectTypeName,
            isOnline,
            cancellationToken);

    public Task<ClientProjectListViewModel> GetHomeClientProjectListAsync(
        bool refresh,
        string? businessUnitName,
        string? statusName,
        string? clientNameSearch,
        string? businessClassName,
        string? projectTypeName = null,
        bool? isOnline = null,
        CancellationToken cancellationToken = default) =>
        _clientProjectList.GetMaterializedListAsync(
            refresh,
            businessUnitName,
            statusName,
            clientNameSearch,
            businessClassName,
            projectTypeName,
            isOnline,
            cancellationToken);

    public Task<ClientProjectListFilterOptionsViewModel> GetHomeClientProjectListFilterOptionsAsync(
        CancellationToken cancellationToken = default) =>
        _clientProjectList.GetFilterOptionsAsync(cancellationToken);

    public Task<HomeClientProjectBulkParseResult> ParseClientProjectImportCsvAsync(Stream csvStream, CancellationToken cancellationToken = default) =>
        _bulkImport.ParseCsvAsync(csvStream, cancellationToken);

    public Task<HomeClientProjectBulkVerifyResult> VerifyClientProjectImportRowsAsync(
        IReadOnlyList<HomeClientProjectImportRowDto> rows,
        CancellationToken cancellationToken = default) =>
        _bulkImport.VerifyRowsAsync(rows, cancellationToken);

    public Task<HomeClientProjectBulkImportResult> CommitClientProjectImportRowsAsync(
        IReadOnlyList<HomeClientProjectImportRowDto> rows,
        CancellationToken cancellationToken = default) =>
        _bulkImport.CommitRowsAsync(rows, cancellationToken);
}
