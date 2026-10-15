namespace ClientManagementSystem.Models;

public sealed class UserMapAddressRow
{
    public int UserID { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public int AddressID { get; set; }
    public string AddressType { get; set; } = "Work";
    public string Address1 { get; set; } = string.Empty;
    public string Address2 { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public DateTime? GeocodedUtc { get; set; }
    public DateTime? ModifiedDate { get; set; }

    public string AddressLine
    {
        get
        {
            var cityLine = string.Join(" ", new[] { State, PostalCode }.Where(s => !string.IsNullOrWhiteSpace(s)));
            var parts = new[] { Address1, Address2, City, cityLine, Country }
                .Where(s => !string.IsNullOrWhiteSpace(s));
            return string.Join(", ", parts);
        }
    }
}

public sealed class UserMapPoint
{
    public int UserID { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string AddressType { get; set; } = string.Empty;
    public string AddressLine { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public bool HasPhoto { get; set; }
    public string? PlotNote { get; set; }
}

public sealed class UserMapResult
{
    public IReadOnlyList<UserMapPoint> Plotted { get; set; } = [];
    public IReadOnlyList<UserMapPoint> Unplotted { get; set; } = [];
}
