namespace ClientManagementSystem.Models;

/// <summary>Mirrors <c>dbo.tProjectRevenue</c> — append-only revenue history per project.</summary>
public sealed class ProjectRevenue
{
    public int RevenueID { get; set; }
    public int ProjectID { get; set; }
    public DateTime EffectiveDate { get; set; }
    public decimal Revenue { get; set; }
    public string? ChangeReason { get; set; }
    public int RecordedBy { get; set; }
    public string? RecordedByName { get; set; }
    public DateTime CreatedDate { get; set; }
}
