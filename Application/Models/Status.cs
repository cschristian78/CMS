namespace ClientManagementSystem.Models;

/// <summary>
/// Mirrors <c>dbo.tStatus</c>. Statuses are grouped under a <see cref="StatusTypeID"/> bucket
/// (Universal / Client / Project / UserGroup / User / Service) and apply to one or more entity
/// types via <c>dbo.tStatusAppliesTo</c>.
/// </summary>
public sealed class Status
{
    public int StatusID { get; set; }
    public int StatusTypeID { get; set; }
    public string? StatusTypeName { get; set; }
    public string? StatusCode { get; set; }
    public string? StatusDescription { get; set; }
    public bool IsActive { get; set; } = true;

    public string DisplayName => string.IsNullOrWhiteSpace(StatusDescription)
        ? StatusCode ?? string.Empty
        : $"{StatusCode} - {StatusDescription}";
}

/// <summary>Mirrors <c>dbo.tStatusType</c>.</summary>
public sealed class StatusType
{
    public int StatusTypeID { get; set; }
    public string TypeName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}
