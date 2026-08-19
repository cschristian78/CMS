namespace ClientManagementSystem.Models;

/// <summary>Soft-deleted / inactive project header used by dashboard Active vs Inactive exhibits.
/// These rows are not in <c>dbo.vProject_Current</c>.</summary>
public sealed class DashboardInactiveProject
{
    public int ProjectID { get; set; }
    public string? ProjectNumber { get; set; }
    public string? ProjectName { get; set; }
    public string? ClientName { get; set; }
    public DateTime? CreatedDate { get; set; }
    public bool IsOnline { get; set; }
}
