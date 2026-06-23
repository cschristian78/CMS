using ClientManagementSystem.Models;

namespace ClientManagementSystem.Services;

public interface IDashboardService
{
    Task<IReadOnlyList<DashboardBusinessUnitCount>> GetClientCountByBusinessUnitAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DashboardBusinessUnitCount>> GetProjectCountByBusinessUnitAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DashboardNamedCount>> GetClientCountByBusinessClassAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DashboardNamedCount>> GetProjectCountByBusinessClassAsync(
        CancellationToken cancellationToken = default);
}
