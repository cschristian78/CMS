namespace ClientManagementSystem.Models;

/// <summary>Mirrors <c>dbo.tClientCategory</c> (renamed from the old <c>tClientTypeCategory</c>).</summary>
public sealed class ClientCategory
{
    public int ClientCategoryID { get; set; }
    public string CCName { get; set; } = string.Empty;
    public string? CCDescription { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? CreatedDate { get; set; }
    public DateTime? ModifiedDate { get; set; }

    public string DisplayName => string.IsNullOrWhiteSpace(CCDescription) ? CCName : $"{CCName} - {CCDescription}";
}

/// <summary>Mirrors <c>dbo.tClientType</c>.</summary>
public sealed class ClientType
{
    public int ClientTypeID { get; set; }
    public string CTName { get; set; } = string.Empty;
    public string? CTDescription { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? CreatedDate { get; set; }
    public DateTime? ModifiedDate { get; set; }

    public string DisplayName => FormatDisplayName(CTName, CTDescription);

    public static string FormatDisplayName(string? ctName, string? ctDescription) =>
        string.IsNullOrWhiteSpace(ctDescription)
            ? (ctName ?? string.Empty).Trim()
            : $"{(ctName ?? string.Empty).Trim()} - {ctDescription.Trim()}";
}
