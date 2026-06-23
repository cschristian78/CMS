namespace ClientManagementSystem.Models;

/// <summary>Save payload for the Admin / Groups tab. Persists a header to <c>dbo.tUserGroup</c>
/// and replaces members in <c>dbo.tUserGroupUser</c>.</summary>
public sealed class SaveUserGroupBatchRequest
{
    /// <summary>Existing group identity (<c>dbo.tUserGroup.UGID</c>); 0 to create new.</summary>
    public int UGID { get; set; }

    /// <summary>Required string identifier for the group; unique on <c>dbo.tUserGroup</c>.</summary>
    public string UserGroupNumber { get; set; } = string.Empty;

    public string? UGName { get; set; }
    public int? UserRoleID { get; set; }
    public int StatusID { get; set; }

    public IReadOnlyList<SaveUserGroupMemberLine> Members { get; set; } = [];
}

public sealed class SaveUserGroupMemberLine
{
    public int UserID { get; set; }
    public int? UserRoleID { get; set; }
    public int StatusID { get; set; }
}
