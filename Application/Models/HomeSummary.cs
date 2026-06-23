namespace ClientManagementSystem.Models;

public sealed class HomeSummary
{
    /// <summary>Count of rows in <c>dbo.tClient</c> where <c>IsActive = 1</c> and <c>ClientStatus = 'Active'</c>.</summary>
    public int UniqueClientCount { get; set; }

    /// <summary>Distinct active <c>ProjectID</c> values in scope for the current filters.</summary>
    public int DistinctProjectCount { get; set; }

    /// <summary>Sum of latest revenue for active clients and active projects in scope.</summary>
    public double TotalProjectRevenue { get; set; }

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
