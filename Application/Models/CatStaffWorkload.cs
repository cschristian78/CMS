namespace ClientManagementSystem.Models;

/// <summary>CAT department user with aggregated time-entry hours.</summary>
public sealed class CatStaffHoursRow
{
    public int UserID { get; set; }
    public string UserFullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public decimal TotalHours { get; set; }
    public int ProjectCount { get; set; }
    public int ClientCount { get; set; }
}

/// <summary>Named hours/revenue slice for CAT analyst summaries.</summary>
public sealed class CatHoursNamedMetric
{
    public string Label { get; set; } = string.Empty;
    public decimal Hours { get; set; }
    public decimal Revenue { get; set; }
}

/// <summary>Client-level hours and revenue for a selected CAT analyst.</summary>
public sealed class CatClientHoursMetric
{
    public int? ClientID { get; set; }
    public string ClientName { get; set; } = string.Empty;
    public decimal Hours { get; set; }
    public decimal LatestRevenue { get; set; }
    public int ProjectCount { get; set; }
}

/// <summary>Selected CAT analyst: hours by role, client, and revenue tier.</summary>
public sealed class CatAnalystWorkloadDetail
{
    public int UserID { get; set; }
    public string UserFullName { get; set; } = string.Empty;
    public decimal TotalHours { get; set; }
    public IReadOnlyList<CatHoursNamedMetric> ByRole { get; set; } = Array.Empty<CatHoursNamedMetric>();
    public IReadOnlyList<CatClientHoursMetric> ByClient { get; set; } = Array.Empty<CatClientHoursMetric>();
    public IReadOnlyList<CatHoursNamedMetric> ByRevenue { get; set; } = Array.Empty<CatHoursNamedMetric>();
}
