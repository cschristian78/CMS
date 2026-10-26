using ClientManagementSystem.ViewModels;

namespace ClientManagementSystem.Services;

/// <summary>
/// Shared read/refresh logic for <c>dbo.vProject_Current</c> (BUName, BCName,
/// ClientStatus, ClientName, PTDescription filters). The legacy materialized
/// <c>dbo.tClientProjectList</c> table is retired in CMS_PhaseI_All — refreshes
/// now invoke <c>dbo.usp_Create_vProject_Current</c>. Consumed by both the home
/// and dashboard HTTP workflows.
/// </summary>
public interface IClientProjectListService
{
    Task<ClientProjectListViewModel> GetMaterializedListAsync(
        bool refresh,
        string? businessUnitName,
        string? statusName,
        string? clientNameSearch,
        string? businessClassName,
        string? projectTypeName = null,
        string? workStatus = null,
        IReadOnlyList<string>? analyticsLeadNames = null,
        CancellationToken cancellationToken = default);

    Task<ClientProjectListFilterOptionsViewModel> GetFilterOptionsAsync(CancellationToken cancellationToken = default);
}
