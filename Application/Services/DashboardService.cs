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

    public Task<IReadOnlyList<DashboardBusinessUnitCount>> GetClientCountByBusinessUnitAsync(
        CancellationToken cancellationToken = default) =>
        _dashboard.GetClientCountByBusinessUnitAsync(cancellationToken);

    public Task<IReadOnlyList<DashboardBusinessUnitCount>> GetProjectCountByBusinessUnitAsync(
        CancellationToken cancellationToken = default) =>
        _dashboard.GetProjectCountByBusinessUnitAsync(cancellationToken);

    public Task<IReadOnlyList<DashboardNamedCount>> GetClientCountByBusinessClassAsync(
        CancellationToken cancellationToken = default) =>
        _dashboard.GetClientCountByBusinessClassAsync(cancellationToken);

    public Task<IReadOnlyList<DashboardNamedCount>> GetProjectCountByBusinessClassAsync(
        CancellationToken cancellationToken = default) =>
        _dashboard.GetProjectCountByBusinessClassAsync(cancellationToken);
}
