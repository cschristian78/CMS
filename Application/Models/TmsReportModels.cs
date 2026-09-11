namespace ClientManagementSystem.Models;

public sealed class TmsNamedHours
{
    public string Label { get; set; } = string.Empty;
    public decimal Hours { get; set; }
    public int? Id { get; set; }
    public string? Detail { get; set; }
    public decimal ProjectHours { get; set; }
    public decimal OtherHours { get; set; }
    public int Count { get; set; }
}

public sealed class TmsMonthHours
{
    public string MonthKey { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public decimal ProjectHours { get; set; }
    public decimal OtherHours { get; set; }
    public decimal TotalHours { get; set; }
}

public sealed class TmsIncompleteWeek
{
    public int UserID { get; set; }
    public string UserFullName { get; set; } = string.Empty;
    public DateTime WeekStartDate { get; set; }
    public decimal Hours { get; set; }
}

public sealed class TmsReportKpis
{
    public decimal TotalHours { get; set; }
    public decimal ProjectHours { get; set; }
    public decimal OtherHours { get; set; }
    public decimal ProjectSharePct { get; set; }
    public int AnalystCount { get; set; }
    public int ProjectCount { get; set; }
    public int ClientCount { get; set; }
    public decimal CapacityHours { get; set; }
    public decimal UtilizationPct { get; set; }
    public int IncompleteWeekCount { get; set; }
    public decimal ExactHours { get; set; }
    public decimal AllocatedHours { get; set; }
}

public sealed class TmsReportResponse
{
    public int Year { get; set; }
    public int? UserID { get; set; }
    public int PriorYear { get; set; }
    public IReadOnlyList<int> AvailableYears { get; set; } = [];
    public IReadOnlyList<TmsCatUser> CatUsers { get; set; } = [];
    public TmsReportKpis Kpis { get; set; } = new();
    public TmsReportKpis PriorYearKpis { get; set; } = new();
    public IReadOnlyList<TmsNamedHours> ByAnalyst { get; set; } = [];
    public IReadOnlyList<TmsNamedHours> ByCategory { get; set; } = [];
    public IReadOnlyList<TmsNamedHours> ByClient { get; set; } = [];
    public IReadOnlyList<TmsNamedHours> ByProject { get; set; } = [];
    public IReadOnlyList<TmsNamedHours> ByTask { get; set; } = [];
    public IReadOnlyList<TmsMonthHours> ByMonth { get; set; } = [];
    public IReadOnlyList<TmsMonthHours> ByMonthPriorYear { get; set; } = [];
    public IReadOnlyList<TmsIncompleteWeek> IncompleteWeeks { get; set; } = [];
}
