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

    public Task<IReadOnlyList<DashboardNamedCount>> GetProjectCountByBeginMonthAsync(
        string? analyticsLeadName = null,
        bool? isOnline = null,
        CancellationToken cancellationToken = default) =>
        _dashboard.GetProjectCountByBeginMonthAsync(analyticsLeadName, isOnline, cancellationToken);
}
