using ClientManagementSystem.Data;
using ClientManagementSystem.Models;
using ClientManagementSystem.ViewModels;

namespace ClientManagementSystem.Services;

public sealed class DashboardService : IDashboardService
{
    private static readonly HashSet<string> StaticGridColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "ProjectOnline", "IsOnline",
        "BusinessUnitName", "BusinessUnitID", "SegmentName", "SegmentID",
        "BusinessClassName", "BusinessClassID",
        "ClientID", "ClientName",
        "StatusDescription", "ClientStatus",
        "ProjectID", "ProjectNumber", "ProjectName",
        "PTDescription",
        "RenewalDate", "DueDate",
        "ProjectStatus",
        "LatestRevenueID", "LatestRevenueEffectiveDate", "LatestRevenue", "RevenueTier", "RevenueAmount",
        "UGID", "UGName", "UserGroupID", "UserGroupName",
        "ProjectRefID", "CreatedDate", "ModifiedDate", "IsActive",
        "TotalHours",
        "ProjectServices",
    };

    private readonly IDashboardRepository _dashboard;
    private readonly IReferenceDataRepository _referenceData;

    public DashboardService(IDashboardRepository dashboard, IReferenceDataRepository referenceData)
    {
        _dashboard = dashboard;
        _referenceData = referenceData;
    }

    public Task<IReadOnlyList<string>> GetAnalyticsLeadFilterOptionsAsync(
        CancellationToken cancellationToken = default) =>
        _dashboard.GetDistinctAnalyticsLeadNamesAsync(cancellationToken);

    public Task<IReadOnlyList<DashboardNamedCount>> GetProjectCountByBeginMonthAsync(
        string? analyticsLeadName = null,
        bool? isOnline = null,
        CancellationToken cancellationToken = default) =>
        _dashboard.GetProjectCountByBeginMonthAsync(analyticsLeadName, isOnline, cancellationToken);

    public async Task<DashboardDatasetViewModel> GetDatasetAsync(CancellationToken cancellationToken = default)
    {
        var raw = await _dashboard.GetClientProjectListTableAsync(
            businessUnitName: null,
            statusName: null,
            clientNameSearch: null,
            businessClassName: null,
            projectTypeName: null,
            workStatus: null,
            analyticsLeadNames: null,
            cancellationToken: cancellationToken);

        var roles = await _referenceData.GetUserRolesAsync(cancellationToken);
        var roleNameSet = roles
            .Select(r => (r.UserRoleName ?? string.Empty).Trim())
            .Where(n => n.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var roleColumns = raw.Columns
            .Where(c => roleNameSet.Contains(c.Trim()))
            .ToList();

        var serviceColumns = raw.Columns
            .Where(c => IsServiceColumn(c, roleNameSet))
            .ToList();

        IReadOnlyList<DashboardInactiveProject> inactive = [];
        try
        {
            inactive = await _dashboard.GetInactiveProjectsAsync(cancellationToken);
        }
        catch
        {
            inactive = [];
        }

        return new DashboardDatasetViewModel
        {
            Columns = raw.Columns,
            Rows = raw.Rows,
            RoleColumns = roleColumns,
            ServiceColumns = serviceColumns,
            InactiveProjects = inactive.Select(p => new DashboardInactiveProjectRow
            {
                ProjectID = p.ProjectID,
                ProjectNumber = p.ProjectNumber,
                ProjectName = p.ProjectName,
                ClientName = p.ClientName,
                CreatedDate = p.CreatedDate,
                IsOnline = p.IsOnline,
            }).ToList(),
        };
    }

    public Task<IReadOnlyList<CatStaffHoursRow>> GetCatStaffHoursAsync(
        CancellationToken cancellationToken = default) =>
        _dashboard.GetCatStaffHoursAsync(cancellationToken);

    public Task<CatAnalystWorkloadDetail?> GetCatAnalystWorkloadDetailAsync(
        int userId,
        CancellationToken cancellationToken = default) =>
        _dashboard.GetCatAnalystWorkloadDetailAsync(userId, cancellationToken);

    private static bool IsServiceColumn(string columnName, HashSet<string> roleNames)
    {
        var name = columnName.Trim();
        if (name.Length == 0 || StaticGridColumns.Contains(name) || roleNames.Contains(name))
        {
            return false;
        }

        return name.Contains('_', StringComparison.Ordinal);
    }
}
