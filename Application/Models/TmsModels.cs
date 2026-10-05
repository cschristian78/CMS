namespace ClientManagementSystem.Models;

/// <summary>CAT department user for Time Management picker.</summary>
public sealed class TmsCatUser
{
    public int UserID { get; set; }
    public string UserFullName { get; set; } = string.Empty;
    public string? Email { get; set; }
}

/// <summary>Project the selected CAT user belongs to (via user group).</summary>
public sealed class TmsUserProject
{
    public int ProjectID { get; set; }
    public int ProjectRefID { get; set; }
    public string ProjectName { get; set; } = string.Empty;
    public string? ClientName { get; set; }
    /// <summary>True when the project is Online / currently in progress.</summary>
    public bool IsOnline { get; set; }
    public IReadOnlyList<ProjectCalendarItem> Tasks { get; set; } = [];
    public IReadOnlyList<ProjectTimeEntry> Entries { get; set; } = [];
}

/// <summary>Non-project time row (Meetings, PTO, Training, etc.).</summary>
public sealed class UserTimeEntry
{
    public int UserTimeEntryID { get; set; }
    public int UserID { get; set; }
    public string Category { get; set; } = string.Empty;
    public DateTime WorkDate { get; set; }
    public decimal Hours { get; set; }
    public string EntryMode { get; set; } = "Exact";
    public string? Notes { get; set; }
}

public sealed class TmsWeekResponse
{
    public DateTime WeekStartDate { get; set; }
    public DateTime WeekEndDate { get; set; }
    public int UserID { get; set; }
    public IReadOnlyList<TmsUserProject> Projects { get; set; } = [];
    public IReadOnlyList<UserTimeEntry> OtherEntries { get; set; } = [];
    public IReadOnlyList<string> OtherCategories { get; set; } = TmsOtherTimeCategories.All;
}

public sealed class TmsProjectWeekSave
{
    public int ProjectID { get; set; }
    public List<ProjectTimeEntry> Entries { get; set; } = [];
}

public sealed class TmsWeekSaveRequest
{
    public DateTime WeekStartDate { get; set; }
    public int UserID { get; set; }
    public List<TmsProjectWeekSave> Projects { get; set; } = [];
    public List<UserTimeEntry> OtherEntries { get; set; } = [];
}

public sealed class TmsWeekSaveResult
{
    public int ProjectSavedCount { get; set; }
    public int OtherSavedCount { get; set; }
    public string Message { get; set; } = string.Empty;
}

/// <summary>Rollup of project + non-client hours for a CAT user across common periods.</summary>
public sealed class TmsHoursSummary
{
    public int UserID { get; set; }
    public decimal ThisWeekHours { get; set; }
    public decimal LastWeekHours { get; set; }
    public decimal MonthHours { get; set; }
    public decimal YtdHours { get; set; }
    public DateTime ThisWeekStart { get; set; }
    public DateTime ThisWeekEnd { get; set; }
    public DateTime LastWeekStart { get; set; }
    public DateTime LastWeekEnd { get; set; }
    public DateTime MonthStart { get; set; }
    public DateTime YtdStart { get; set; }
    public DateTime AsOfDate { get; set; }
}

public static class TmsOtherTimeCategories
{
    public static readonly IReadOnlyList<string> All =
    [
        "Meetings",
        "Holidays",
        "PTO",
        "Travel",
        "Training",
        "CSI",
        "Moodys MAT",
        "Verisk MAT",
        "Peril Advisory",
        "Administrative",
    ];

    public static bool IsValid(string? category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return false;
        }

        return All.Any(c => string.Equals(c, category.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public static string Normalize(string category)
    {
        var match = All.FirstOrDefault(c => string.Equals(c, category.Trim(), StringComparison.OrdinalIgnoreCase));
        return match ?? category.Trim();
    }
}
