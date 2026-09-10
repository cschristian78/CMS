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

    /// <summary>Total hours by active user in the CAT department.</summary>
    Task<IReadOnlyList<CatStaffHoursRow>> GetCatStaffHoursAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Hours by role / client / revenue tier for one CAT analyst.</summary>
    Task<CatAnalystWorkloadDetail?> GetCatAnalystWorkloadDetailAsync(
        int userId,
        CancellationToken cancellationToken = default);
}
