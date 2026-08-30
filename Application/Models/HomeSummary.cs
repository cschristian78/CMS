namespace ClientManagementSystem.Models;

public sealed class HomeSummary
{
    /// <summary>Distinct active <c>ProjectID</c> values in <c>dbo.vProject_Current</c> with Active
    /// client status and Active project (same scope as Total Revenue).</summary>
    public int ActiveProjectCount { get; set; }

    /// <summary>Distinct active <c>ProjectID</c> values in scope for the current Main list filters.</summary>
    public int DistinctProjectCount { get; set; }

    /// <summary>Sum of latest revenue for all Active clients and Active projects (ignores Main list filters).</summary>
    public double TotalProjectRevenue { get; set; }

    /// <summary>Distinct active projects with <c>tProject.IsOnline = 1</c> (global; ignores list filters).</summary>
    public int OnlineProjectCount { get; set; }

    /// <summary>Sum of latest revenue for those online active projects.</summary>
    public double OnlineProjectRevenue { get; set; }

    /// <summary>Active project counts grouped by <c>ProjectTypeDescription</c>.</summary>
    public IReadOnlyList<HomeProjectTypeCount> ProjectTypeCounts { get; set; } = [];
}

public sealed class HomeProjectTypeCount
{
    public int ProjectTypeId { get; set; }
    public string? ProjectTypeCode { get; set; }
    public string? ProjectTypeDescription { get; set; }
    public int ProjectCount { get; set; }
}
