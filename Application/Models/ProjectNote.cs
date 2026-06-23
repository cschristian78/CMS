namespace ClientManagementSystem.Models;

/// <summary>Mirrors <c>dbo.tProjectNote</c>.</summary>
public sealed class ProjectNote
{
    public int NoteID { get; set; }
    public int ProjectID { get; set; }
    public int UserID { get; set; }
    public string? UserFullName { get; set; }
    public DateTime NoteDate { get; set; }
    public string NoteType { get; set; } = "General";
    public string NoteText { get; set; } = string.Empty;
    public bool IsPinned { get; set; }
    public DateTime CreatedDate { get; set; }
    public DateTime? ModifiedDate { get; set; }
}
