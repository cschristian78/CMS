namespace ClientManagementSystem.Models;

/// <summary>Mirrors a row from <c>dbo.tUserGroupUser</c> joined to the group header and the user.
/// In the CMS_PhaseI_All schema membership is its own table with per-member
/// <see cref="UserRoleID"/> and <see cref="StatusID"/>.</summary>
public sealed class UserGroupMember
{
    /// <summary>Owning group (FK to <c>dbo.tUserGroup.UGID</c>).</summary>
    public int UGID { get; set; }
    public string? UserGroupNumber { get; set; }
    public string? UGName { get; set; }

    public int UserID { get; set; }
    public string? Username { get; set; }
    public string? UserFullName { get; set; }
    public string? Title { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }

    public int? UserRoleID { get; set; }
    public string? UserRoleName { get; set; }

    public int StatusID { get; set; }
    public string? StatusCode { get; set; }
    public string? StatusDescription { get; set; }

    public DateTime JoinedDate { get; set; }

    public string DisplayName =>
        $"{UGName} | {(string.IsNullOrWhiteSpace(Username) ? UserFullName : Username)} | {UserRoleName}";
}
