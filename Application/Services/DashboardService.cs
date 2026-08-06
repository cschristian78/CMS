using ClientManagementSystem.Data;
using ClientManagementSystem.Models;

namespace ClientManagementSystem.Services;

public sealed class DashboardService : IDashboardService
{
    private readonly IDashboardRepository _dashboard;

    public DashboardService(IDashboardRepository dashboard)
    {
        _dashboard = dashboard;
    }

    public Task<IReadOnlyList<string>> GetAnalyticsLeadFilterOptionsAsync(
        CancellationToken cancellationToken = default) =>
        _dashboard.GetDistinctAnalyticsLeadNamesAsync(cancellationToken);

    public Task<IReadOnlyList<DashboardBusinessUnitCount>> GetClientCountByBusinessUnitAsync(
        string? analyticsLeadName = null,
        CancellationToken cancellationToken = default) =>
        _dashboard.GetClientCountByBusinessUnitAsync(analyticsLeadName, cancellationToken);

    public Task<IReadOnlyList<DashboardBusinessUnitCount>> GetProjectCountByBusinessUnitAsync(
        string? analyticsLeadName = null,
        CancellationToken cancellationToken = default) =>
        _dashboard.GetProjectCountByBusinessUnitAsync(analyticsLeadName, cancellationToken);

    public Task<IReadOnlyList<DashboardNamedCount>> GetClientCountByBusinessClassAsync(
        string? analyticsLeadName = null,
        CancellationToken cancellationToken = default) =>
        _dashboard.GetClientCountByBusinessClassAsync(analyticsLeadName, cancellationToken);

    public Task<IReadOnlyList<DashboardNamedCount>> GetProjectCountByBusinessClassAsync(
        string? analyticsLeadName = null,
        CancellationToken cancellationToken = default) =>
        _dashboard.GetProjectCountByBusinessClassAsync(analyticsLeadName, cancellationToken);
}
