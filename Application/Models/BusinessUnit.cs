namespace ClientManagementSystem.Models;

/// <summary>Mirrors <c>dbo.tBusinessUnit</c>.</summary>
public sealed class BusinessUnit
{
    public int BusinessUnitID { get; set; }
    public string? BusinessUnitName { get; set; }
    public string? BusinessUnitDescription { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? CreatedDate { get; set; }
    public DateTime? ModifiedDate { get; set; }

    public string DisplayName => string.IsNullOrWhiteSpace(BusinessUnitName) ? $"BU {BusinessUnitID}" : BusinessUnitName!;
}
