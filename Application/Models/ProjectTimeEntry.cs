namespace ClientManagementSystem.Models;

/// <summary>Row in <c>dbo.tProjectTimeEntry</c> — hours logged against a calendar service/task.</summary>
public sealed class ProjectTimeEntry
{
    public int TimeEntryID { get; set; }
    public int ProjectID { get; set; }
    public int ProjectRefID { get; set; }
    public int CalendarItemID { get; set; }
    public string? CalendarItemTitle { get; set; }
    public string? CalendarItemType { get; set; }
    public DateTime WorkDate { get; set; }
    public decimal Hours { get; set; }

    /// <summary>Exact | Allocated</summary>
    public string EntryMode { get; set; } = "Exact";

    public string? Notes { get; set; }
    public int UserID { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? CreatedDate { get; set; }
    public DateTime? ModifiedDate { get; set; }
}

/// <summary>Week payload for GET/PUT project time tracker.</summary>
public sealed class ProjectTimeWeekRequest
{
    public DateTime WeekStartDate { get; set; }
    /// <summary>User whose hours are being saved (CAT team member or acting user).</summary>
    public int? UserID { get; set; }
    public List<ProjectTimeEntry> Entries { get; set; } = [];
}

/// <summary>Week response including tracker rows for the UI.</summary>
public sealed class ProjectTimeWeekResponse
{
    public DateTime WeekStartDate { get; set; }
    public DateTime WeekEndDate { get; set; }
    public int? UserID { get; set; }
    public IReadOnlyList<ProjectTimeCatUser> CatUsers { get; set; } = [];
    public IReadOnlyList<ProjectCalendarItem> Tasks { get; set; } = [];
    public IReadOnlyList<ProjectTimeEntry> Entries { get; set; } = [];
}

/// <summary>Project-level time rollups for reports (all weeks / all analysts).</summary>
public sealed class ProjectTimeSummary
{
    public IReadOnlyList<ProjectTimeSummaryRow> ByAnalyst { get; set; } = [];
    public IReadOnlyList<ProjectTimeSummaryRow> ByTask { get; set; } = [];
    public decimal TotalHours { get; set; }
}

public sealed class ProjectTimeSummaryRow
{
    public int Id { get; set; }
    public string Label { get; set; } = string.Empty;
    public string? Detail { get; set; }
    public decimal Hours { get; set; }
}
