namespace ClientManagementSystem.Models;

/// <summary>Mirrors <c>dbo.tBusinessClass</c>.</summary>
public sealed class BusinessClass
{
    public int BusinessClassID { get; set; }
    public string? BusinessClassName { get; set; }
    public string? BusinessClassDescription { get; set; }
    public int BusinessUnitID { get; set; }
    public string? BusinessUnitName { get; set; }
    public DateTime? CreatedDate { get; set; }
    public DateTime? ModifiedDate { get; set; }

    /// <summary>Prefer <see cref="BusinessClassDescription"/> for UI labels when present.</summary>
    public static string FormatDisplayDescription(string? name, string? description) =>
        string.IsNullOrWhiteSpace(description)
            ? (name ?? string.Empty).Trim()
            : description.Trim();
}
