namespace ClientManagementSystem.Models;

/// <summary>Mirrors <c>dbo.tProjectService</c> — junction between projects and services with
/// per-link metadata.</summary>
public sealed class ProjectService
{
    public int ProjectID { get; set; }
    public int ServiceID { get; set; }
    public string? ServiceName { get; set; }
    public string? CategoryName { get; set; }
    public string? Frequency { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public DateTime CreatedDate { get; set; }
}
