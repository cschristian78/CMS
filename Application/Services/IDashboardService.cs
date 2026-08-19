using ClientManagementSystem.Models;
using ClientManagementSystem.ViewModels;

namespace ClientManagementSystem.Services;

public interface IDashboardService
{
    Task<IReadOnlyList<string>> GetAnalyticsLeadFilterOptionsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Project counts by begin month (<c>tProject.CreatedDate</c>), optional Analytics Lead and Online filters.</summary>
    Task<IReadOnlyList<DashboardNamedCount>> GetProjectCountByBeginMonthAsync(
        string? analyticsLeadName = null,
        bool? isOnline = null,
        CancellationToken cancellationToken = default);

    /// <summary>Full <c>vProject_Current</c> grid plus TotalHours, role/service column lists, and inactive projects.</summary>
    Task<DashboardDatasetViewModel> GetDatasetAsync(CancellationToken cancellationToken = default);
}
