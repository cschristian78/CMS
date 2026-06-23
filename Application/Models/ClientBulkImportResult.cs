namespace ClientManagementSystem.Models;

public sealed class ClientBulkImportResult
{
    public int ImportedCount { get; set; }
    public int SkippedEmptyRows { get; set; }
    public IReadOnlyList<string> RowErrors { get; set; } = [];
}
