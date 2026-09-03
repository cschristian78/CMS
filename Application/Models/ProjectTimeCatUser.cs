namespace ClientManagementSystem.Models;

/// <summary>User group member on a project whose <c>Department</c> is CAT.</summary>
public sealed class ProjectTimeCatUser
{
    public int UserID { get; set; }
    public string UserFullName { get; set; } = string.Empty;
    public string? Email { get; set; }
}
