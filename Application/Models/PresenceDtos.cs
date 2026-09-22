namespace ClientManagementSystem.Models;

public sealed class PresenceUserDto
{
    public int UserID { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool HasPhoto { get; set; }
    public DateTimeOffset LastSeenUtc { get; set; }
}

public sealed class PresenceSnapshotDto
{
    public IReadOnlyList<PresenceUserDto> Users { get; set; } = Array.Empty<PresenceUserDto>();
}
