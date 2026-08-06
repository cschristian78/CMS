using ClientManagementSystem.Models;

namespace ClientManagementSystem.Services;

public interface IDashboardService
{
    Task<IReadOnlyList<string>> GetAnalyticsLeadFilterOptionsAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DashboardBusinessUnitCount>> GetClientCountByBusinessUnitAsync(
        string? analyticsLeadName = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DashboardBusinessUnitCount>> GetProjectCountByBusinessUnitAsync(
        string? analyticsLeadName = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DashboardNamedCount>> GetClientCountByBusinessClassAsync(
        string? analyticsLeadName = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DashboardNamedCount>> GetProjectCountByBusinessClassAsync(
        string? analyticsLeadName = null,
        CancellationToken cancellationToken = default);
}
