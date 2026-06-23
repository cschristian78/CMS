namespace ClientManagementSystem.Models;

/// <summary>Mirrors <c>dbo.tProjectType</c>. Columns renamed from <c>ProjectTypeCode/Description</c>
/// to <c>PTCode/PTDescription</c> in the CMS_PhaseI_All schema.</summary>
public sealed class ProjectType
{
    public int ProjectTypeID { get; set; }
    public string? PTCode { get; set; }
    public string? PTDescription { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? CreatedDate { get; set; }
    public DateTime? ModifiedDate { get; set; }

    public string DisplayName => string.IsNullOrWhiteSpace(PTDescription)
        ? PTCode ?? string.Empty
        : $"{PTCode} - {PTDescription}";
}
