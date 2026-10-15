using System.ComponentModel.DataAnnotations;

namespace ClientManagementSystem.Models;

/// <summary>Mirrors <c>dbo.tUserAddress</c>. New 1:N child of <see cref="User"/>.</summary>
public sealed class UserAddress
{
    public int AddressID { get; set; }
    public int UserID { get; set; }

    [MaxLength(20)]
    public string AddressType { get; set; } = "Work";

    [MaxLength(200)]
    public string Address1 { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? Address2 { get; set; }

    [MaxLength(100)]
    public string City { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? State { get; set; }

    [MaxLength(20)]
    public string? PostalCode { get; set; }

    [MaxLength(50)]
    public string Country { get; set; } = "US";

    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    public DateTime? CreatedDate { get; set; }
    public DateTime? ModifiedDate { get; set; }
}
