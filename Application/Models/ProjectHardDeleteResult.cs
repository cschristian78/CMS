namespace ClientManagementSystem.Models;

/// <summary>Result of <c>dbo.usp_Project_HardDelete</c>. Blob paths are captured before
/// SQL rows are removed so Azure cleanup can run afterward.</summary>
public sealed class ProjectHardDeleteResult
{
    public int ProjectID { get; set; }
    public string ProjectNumber { get; set; } = string.Empty;
    public string? ProjectName { get; set; }
    public IReadOnlyList<string> BlobPaths { get; set; } = [];
}
