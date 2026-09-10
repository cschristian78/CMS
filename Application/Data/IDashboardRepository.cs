using ClientManagementSystem.Models;

namespace ClientManagementSystem.Data;

/// <summary>
/// Dashboard analytics and shared read/refresh access to <c>dbo.vProject_Current</c> for home workflows.
/// </summary>
public interface IDashboardRepository
{
    Task<IReadOnlyList<string>> GetDistinctAnalyticsLeadNamesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Distinct project counts by project begin month (<c>tProject.CreatedDate</c>).</summary>
    Task<IReadOnlyList<DashboardNamedCount>> GetProjectCountByBeginMonthAsync(
        string? analyticsLeadName = null,
        bool? isOnline = null,
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

    Task RefreshClientProjectListAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetClientProjectListDistinctColumnValuesAsync(
        string columnName,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetDistinctClientNamesForFiltersAsync(CancellationToken cancellationToken = default);

    Task<RawSqlTableResult> GetClientProjectListTableAsync(
        string? businessUnitName,
        string? statusName,
        string? clientNameSearch,
        string? businessClassName,
        string? projectTypeName = null,
        bool? isOnline = null,
        IReadOnlyList<string>? analyticsLeadNames = null,
        CancellationToken cancellationToken = default);

    /// <summary>Inactive <c>tProject</c> rows that <c>vProject_Current</c> omits.</summary>
    Task<IReadOnlyList<DashboardInactiveProject>> GetInactiveProjectsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Total hours by active user in the CAT department.</summary>
    Task<IReadOnlyList<CatStaffHoursRow>> GetCatStaffHoursAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Hours by role / client / revenue tier for one CAT analyst.</summary>
    Task<CatAnalystWorkloadDetail?> GetCatAnalystWorkloadDetailAsync(
        int userId,
        CancellationToken cancellationToken = default);
}
