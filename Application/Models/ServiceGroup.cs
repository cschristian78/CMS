namespace ClientManagementSystem.Models;

/// <summary>Mirrors <c>dbo.tServiceGroup</c>. Lookup used to group services on a
/// <c>tServiceRef</c> row (CatFunction → ServiceCategory → ServiceGroup → Service).</summary>
public sealed class ServiceGroup
{
    public int ServiceGroupID { get; set; }
    public string ServiceGroupNumber { get; set; } = string.Empty;
    public string ServiceGroupName { get; set; } = string.Empty;
    public string? ServiceGroupDescription { get; set; }
    public bool IsActive { get; set; } = true;

    public string DisplayName => string.IsNullOrWhiteSpace(ServiceGroupName)
        ? $"Group {ServiceGroupID}"
        : ServiceGroupName;
}
