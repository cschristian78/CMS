using System.Text.Json.Serialization;

namespace ClientManagementSystem.Models;

/// <summary>Mirrors <c>dbo.tProjectType</c> (<c>ProjectTypeCode</c> / <c>ProjectTypeDescription</c>).
/// Admin UI posts camelCase <c>projectTypeCode</c> / <c>projectTypeDescription</c>.</summary>
public sealed class ProjectType
{
    public int ProjectTypeID { get; set; }

    [JsonPropertyName("projectTypeCode")]
    public string? PTCode { get; set; }

    [JsonPropertyName("projectTypeDescription")]
    public string? PTDescription { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime? CreatedDate { get; set; }
    public DateTime? ModifiedDate { get; set; }

    public string DisplayName => string.IsNullOrWhiteSpace(PTDescription)
        ? PTCode ?? string.Empty
        : $"{PTCode} - {PTDescription}";
}
