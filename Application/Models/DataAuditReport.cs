namespace ClientManagementSystem.Models;

/// <summary>Admin Data Audit report for Client → Project → User Group → User linkage gaps.</summary>
public sealed class DataAuditReport
{
    public DateTimeOffset GeneratedAtUtc { get; set; }
    public IReadOnlyList<DataAuditCheck> Checks { get; set; } = [];
}

public sealed class DataAuditCheck
{
    public string CheckId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Count { get; set; }
    public IReadOnlyList<string> Columns { get; set; } = [];
    public IReadOnlyList<IReadOnlyList<string?>> Rows { get; set; } = [];
}
