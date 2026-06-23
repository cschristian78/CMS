namespace ClientManagementSystem.Models;

/// <summary>Mirrors <c>dbo.tUserGroup</c> header (one row per group). Membership now lives in
/// <c>dbo.tUserGroupUser</c> — see <see cref="UserGroupMember"/>.</summary>
public sealed class UserGroup
{
    public int UGID { get; set; }
    public string UserGroupNumber { get; set; } = string.Empty;
    public string UGName { get; set; } = string.Empty;
    public int? UserRoleID { get; set; }
    public string? UserRoleName { get; set; }
    public int StatusID { get; set; }
    public string? StatusCode { get; set; }
    public string? StatusDescription { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedDate { get; set; }
    public DateTime? CreatedDate { get; set; }
    public DateTime? ModifiedDate { get; set; }

    public string DisplayName => string.IsNullOrWhiteSpace(UserGroupNumber)
        ? UGName
        : $"{UserGroupNumber} - {UGName}";
}
