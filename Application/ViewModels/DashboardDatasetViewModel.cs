namespace ClientManagementSystem.ViewModels;

/// <summary>Wide project rows from <c>dbo.vProject_Current</c> plus <c>TotalHours</c>
/// (same join as the home list). Role and service column names are classified so the
/// dashboard can slice without charting join keys.</summary>
public sealed class DashboardDatasetViewModel
{
    public IReadOnlyList<string> Columns { get; set; } = [];

    public IReadOnlyList<IReadOnlyList<object?>> Rows { get; set; } = [];

    public IReadOnlyList<string> RoleColumns { get; set; } = [];

    public IReadOnlyList<string> ServiceColumns { get; set; } = [];

    /// <summary>Projects excluded from <c>vProject_Current</c> (<c>tProject.IsActive = 0</c>)
    /// so Active vs Inactive exhibits can still drill down.</summary>
    public IReadOnlyList<DashboardInactiveProjectRow> InactiveProjects { get; set; } = [];
}

public sealed class DashboardInactiveProjectRow
{
    public int ProjectID { get; set; }

    public string? ProjectNumber { get; set; }

    public string? ProjectName { get; set; }

    public string? ClientName { get; set; }

    public DateTime? CreatedDate { get; set; }

    public bool IsOnline { get; set; }
}
