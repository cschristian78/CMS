namespace ClientManagementSystem.Models;

/// <summary>Mirrors <c>dbo.tUserRole</c>. Adds <c>RoleDescription</c> and <c>Tag1/Tag2/Tag3</c>
/// inline tag columns in the CMS_PhaseI_All schema.</summary>
public sealed class UserRole
{
    public int UserRoleID { get; set; }
    public string? UserRoleName { get; set; }
    public string? RoleDescription { get; set; }
    public string? Tag1 { get; set; }
    public string? Tag2 { get; set; }
    public string? Tag3 { get; set; }
    public int? StatusID { get; set; }
    public string? StatusCode { get; set; }
    public string? StatusDescription { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? CreatedDate { get; set; }
    public DateTime? ModifiedDate { get; set; }
}
