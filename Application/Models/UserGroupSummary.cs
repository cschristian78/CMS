namespace ClientManagementSystem.Models;

/// <summary>Lightweight group-roster row aggregated from <c>dbo.tUserGroup</c> +
/// <c>dbo.tUserGroupUser</c>.</summary>
public sealed class UserGroupSummary
{
    public int UGID { get; set; }
    public string UserGroupNumber { get; set; } = string.Empty;
    public string? UGName { get; set; }
    public int MemberCount { get; set; }

    public string DisplayName => $"{UGName} ({MemberCount})";
}
