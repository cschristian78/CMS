using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using ClientManagementSystem.Data;
using ClientManagementSystem.Models;

namespace ClientManagementSystem.Services;

public interface IUserAddressMapService
{
    Task<UserMapResult> GetAsync(string? department, string? addressType, CancellationToken cancellationToken = default);

    /// <summary>Looks up and stores coordinates for one saved address.</summary>
    Task GeocodeStoredAddressAsync(int addressId, CancellationToken cancellationToken = default);
}

/// <summary>Plots employee addresses the way DPO plots locations: one point per address, cached coordinates.</summary>
public sealed class UserAddressMapService : IUserAddressMapService
{
    private static readonly string[] AddressTypes = ["Home", "Work"];

    private readonly IAdminRepository _admin;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IUserPhotoStorage _photos;

    public UserAddressMapService(
        IAdminRepository admin,
        IHttpClientFactory httpClientFactory,
        IUserPhotoStorage photos)
    {
        _admin = admin;
        _httpClientFactory = httpClientFactory;
        _photos = photos;
    }

    public async Task<UserMapResult> GetAsync(string? department, string? addressType, CancellationToken cancellationToken = default)
    {
        var type = NormalizeAddressType(addressType);
        await _admin.EnsureUserAddressMapSchemaAsync(cancellationToken);
        var rows = await _admin.ListUserMapAddressesAsync(department, type, cancellationToken);

        foreach (var row in rows)
        {
            if (row.AddressID <= 0 || (row.Latitude is not null && row.Longitude is not null))
            {
                continue;
            }

            if (!NeedsGeocode(row))
            {
                continue;
            }

            var located = await TryGeocodeAsync(row, cancellationToken);
            row.Latitude = located?.Latitude;
            row.Longitude = located?.Longitude;
            row.GeocodedUtc = DateTime.UtcNow;
            await _admin.SaveAddressGeocodeAsync(row.AddressID, located?.Latitude, located?.Longitude, cancellationToken);
        }

        var plotted = new List<UserMapPoint>();
        var unplotted = new List<UserMapPoint>();
        foreach (var row in rows)
        {
            var point = ToPoint(row);
            if (row.Latitude is not null && row.Longitude is not null)
            {
                plotted.Add(point);
                continue;
            }

            point.PlotNote = row.AddressID <= 0
                ? $"No {row.AddressType.ToLowerInvariant()} address on file."
                : !HasGeocodeInput(row)
                    ? "No street address on file."
                    : string.IsNullOrWhiteSpace(row.Address1) && !string.IsNullOrWhiteSpace(row.City)
                        ? "The city center could not be located."
                        : string.IsNullOrWhiteSpace(row.Address1)
                            ? "The postal code center could not be located."
                            : "This address could not be located.";
            unplotted.Add(point);
        }

        return new UserMapResult { Plotted = plotted, Unplotted = unplotted };
    }

    public async Task GeocodeStoredAddressAsync(int addressId, CancellationToken cancellationToken = default)
    {
        if (addressId <= 0)
        {
            return;
        }

        await _admin.EnsureUserAddressMapSchemaAsync(cancellationToken);
        var row = await _admin.GetAddressForGeocodeAsync(addressId, cancellationToken);
        if (row is null || !HasGeocodeInput(row))
        {
            return;
        }

        var located = await TryGeocodeAsync(row, cancellationToken);
        if (located is null)
        {
            return;
        }

        await _admin.SaveAddressGeocodeAsync(addressId, located.Latitude, located.Longitude, cancellationToken);
    }

    private static bool NeedsGeocode(UserMapAddressRow row)
    {
        if (!HasGeocodeInput(row))
        {
            return false;
        }

        if (row.Latitude is null || row.Longitude is null)
        {
            return true;
        }

        return row.ModifiedDate is not null
            && row.GeocodedUtc is not null
            && row.ModifiedDate.Value > row.GeocodedUtc.Value;
    }

    private async Task<GeoPoint?> TryGeocodeAsync(UserMapAddressRow row, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(row.Address1))
            {
                await Task.Delay(1100, cancellationToken);
                if (!string.IsNullOrWhiteSpace(row.City))
                {
                    var city = await GeocodeCityCentroidAsync(row, cancellationToken);
                    if (city is not null || string.IsNullOrWhiteSpace(row.PostalCode))
                    {
                        return city;
                    }

                    await Task.Delay(1100, cancellationToken);
                }

                return await GeocodePostalCentroidAsync(row, cancellationToken);
            }

            if (IsUnitedStates(row.Country))
            {
                var census = await GeocodeCensusAsync(GeocodeQuery(row), cancellationToken);
                if (census is not null)
                {
                    return census;
                }
            }

            await Task.Delay(1100, cancellationToken);
            return await GeocodeNominatimAsync(GeocodeQuery(row), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private async Task<GeoPoint?> GeocodeCensusAsync(string address, CancellationToken cancellationToken)
    {
        var url =
            "https://geocoding.geo.census.gov/geocoder/locations/onelineaddress?benchmark=Public_AR_Current&format=json&address="
            + Uri.EscapeDataString(address);
        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(12);
        using var response = await client.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!doc.RootElement.TryGetProperty("result", out var result)
            || !result.TryGetProperty("addressMatches", out var matches)
            || matches.GetArrayLength() == 0)
        {
            return null;
        }

        var coordinates = matches[0].GetProperty("coordinates");
        return new GeoPoint(coordinates.GetProperty("y").GetDouble(), coordinates.GetProperty("x").GetDouble());
    }

    private async Task<GeoPoint?> GeocodeNominatimAsync(string address, CancellationToken cancellationToken)
    {
        var url = "https://nominatim.openstreetmap.org/search?format=jsonv2&limit=1&q=" + Uri.EscapeDataString(address);
        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(12);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ClientManagementSystem/1.3 (employee address map)");
        using var response = await client.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var matches = await response.Content.ReadFromJsonAsync<List<NominatimMatch>>(cancellationToken);
        var match = matches?.FirstOrDefault();
        if (match is null
            || !double.TryParse(match.Lat, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
            || !double.TryParse(match.Lon, NumberStyles.Float, CultureInfo.InvariantCulture, out var lon))
        {
            return null;
        }

        return new GeoPoint(lat, lon);
    }

    /// <summary>City, state, and postal code with no street. The result is the city center, not a street interpolation.</summary>
    private async Task<GeoPoint?> GeocodeCityCentroidAsync(UserMapAddressRow row, CancellationToken cancellationToken)
    {
        var country = IsUnitedStates(row.Country)
            ? "United States"
            : row.Country.Trim();
        var structured = BuildNominatimUrl(new Dictionary<string, string?>
        {
            ["featuretype"] = "city",
            ["city"] = row.City.Trim(),
            ["state"] = NullIfBlank(row.State),
            ["postalcode"] = NullIfBlank(row.PostalCode),
            ["country"] = NullIfBlank(country)
        });
        var centroid = await ReadNominatimAsync(structured, cancellationToken);
        if (centroid is not null)
        {
            return centroid;
        }

        await Task.Delay(1100, cancellationToken);
        var query = string.Join(", ", new[] { row.City, row.State, row.PostalCode, country }
            .Select(NullIfBlank)
            .Where(part => part is not null));
        return await ReadNominatimAsync(
            "https://nominatim.openstreetmap.org/search?format=jsonv2&limit=1&q=" + Uri.EscapeDataString(query),
            cancellationToken);
    }

    private static string BuildNominatimUrl(IReadOnlyDictionary<string, string?> fields)
    {
        var parts = new List<string> { "format=jsonv2", "limit=1" };
        foreach (var field in fields)
        {
            if (string.IsNullOrWhiteSpace(field.Value))
            {
                continue;
            }

            parts.Add(field.Key + "=" + Uri.EscapeDataString(field.Value.Trim()));
        }

        return "https://nominatim.openstreetmap.org/search?" + string.Join("&", parts);
    }

    private async Task<GeoPoint?> ReadNominatimAsync(string url, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(12);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ClientManagementSystem/1.3 (employee address map)");
        using var response = await client.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var matches = await response.Content.ReadFromJsonAsync<List<NominatimMatch>>(cancellationToken);
        var match = matches?.FirstOrDefault();
        if (match is null
            || !double.TryParse(match.Lat, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
            || !double.TryParse(match.Lon, NumberStyles.Float, CultureInfo.InvariantCulture, out var lon))
        {
            return null;
        }

        return new GeoPoint(lat, lon);
    }

    private static bool HasGeocodeInput(UserMapAddressRow row) =>
        !string.IsNullOrWhiteSpace(row.Address1)
        || !string.IsNullOrWhiteSpace(row.City)
        || !string.IsNullOrWhiteSpace(row.PostalCode);

    /// <summary>Postal code with no street and no city. The result is the postal-code center.</summary>
    private async Task<GeoPoint?> GeocodePostalCentroidAsync(UserMapAddressRow row, CancellationToken cancellationToken)
    {
        var country = IsUnitedStates(row.Country)
            ? "United States"
            : row.Country.Trim();
        var structured = BuildNominatimUrl(new Dictionary<string, string?>
        {
            ["postalcode"] = row.PostalCode.Trim(),
            ["state"] = NullIfBlank(row.State),
            ["country"] = NullIfBlank(country)
        });
        var centroid = await ReadNominatimAsync(structured, cancellationToken);
        if (centroid is not null)
        {
            return centroid;
        }

        await Task.Delay(1100, cancellationToken);
        var query = string.Join(", ", new[] { row.PostalCode, row.State, country }
            .Select(NullIfBlank)
            .Where(part => part is not null));
        return await ReadNominatimAsync(
            "https://nominatim.openstreetmap.org/search?format=jsonv2&limit=1&q=" + Uri.EscapeDataString(query),
            cancellationToken);
    }

    /// <summary>Suite, unit, and apartment text keeps the Census geocoder and Nominatim from matching the street.</summary>
    private static string GeocodeQuery(UserMapAddressRow row)
    {
        var street = System.Text.RegularExpressions.Regex.Replace(
            row.Address1,
            @"(?i),?\s*\b(suite|ste|unit|apt|apartment|#)\b.*$",
            "").Trim().TrimEnd(',');
        var cityLine = string.Join(" ", new[] { row.State, row.PostalCode }.Where(part => !string.IsNullOrWhiteSpace(part)));
        return string.Join(", ", new[] { street, row.City, cityLine }.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private static string? NullIfBlank(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private UserMapPoint ToPoint(UserMapAddressRow row) => new()
    {
        UserID = row.UserID,
        Name = row.Name,
        Department = row.Department,
        AddressType = row.AddressType,
        AddressLine = row.AddressLine,
        Country = row.Country,
        Latitude = row.Latitude,
        Longitude = row.Longitude,
        HasPhoto = _photos.TryGetPhotoFile(row.UserID, out _, out _)
    };

    private static string NormalizeAddressType(string? value)
    {
        var raw = (value ?? string.Empty).Trim();
        foreach (var allowed in AddressTypes)
        {
            if (string.Equals(raw, allowed, StringComparison.OrdinalIgnoreCase))
            {
                return allowed;
            }
        }

        return "Work";
    }

    private static bool IsUnitedStates(string? country)
    {
        var value = (country ?? string.Empty).Trim();
        return value.Length == 0
            || value.Equals("US", StringComparison.OrdinalIgnoreCase)
            || value.Equals("USA", StringComparison.OrdinalIgnoreCase)
            || value.Equals("United States", StringComparison.OrdinalIgnoreCase)
            || value.Equals("United States of America", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record GeoPoint(double Latitude, double Longitude);

    private sealed class NominatimMatch
    {
        public string? Lat { get; set; }
        public string? Lon { get; set; }
    }
}
