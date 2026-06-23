namespace ClientManagementSystem.Models;

/// <summary>Mirrors <c>dbo.tActivityLog</c>. Written via <c>usp_ActivityLog_Insert</c> on status
/// changes, revenue changes, and soft-delete/restore (per the explicit instrumentation policy).
/// User attribution was intentionally dropped — the log captures what/when, not who.</summary>
public sealed class ActivityLog
{
    public long LogID { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public int EntityID { get; set; }
    public DateTime ActionDate { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? FieldName { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
}
