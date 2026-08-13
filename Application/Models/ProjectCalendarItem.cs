namespace ClientManagementSystem.Models;

/// <summary>Row in <c>dbo.tProjectCalendarItem</c> — Analytics Calendar entry for a project.</summary>
public sealed class ProjectCalendarItem
{
    public int CalendarItemID { get; set; }
    public int ProjectID { get; set; }
    public int ProjectRefID { get; set; }

    /// <summary>Deliverable | Meeting | Task | Other</summary>
    public string ItemType { get; set; } = "Task";

    public string Title { get; set; } = string.Empty;
    public int? OwnerUserID { get; set; }
    public string? OwnerFullName { get; set; }

    /// <summary>Date-only; JSON typically posts as <c>yyyy-MM-dd</c>.</summary>
    public DateTime? ReceivedDate { get; set; }

    /// <summary>Date-only; JSON typically posts as <c>yyyy-MM-dd</c>.</summary>
    public DateTime? CompletedDate { get; set; }

    public string? Notes { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? CreatedDate { get; set; }
    public DateTime? ModifiedDate { get; set; }
}
