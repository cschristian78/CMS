namespace ClientManagementSystem.Models;

/// <summary>Mirrors <c>dbo.tClient</c> + <c>dbo.vClient_WithBU</c>. The legacy link tables
/// (<c>tClientTypeCategoryLink</c>, <c>tClientTypeLink</c>) are retired; <c>tClient</c> now carries
/// direct FKs to <see cref="BusinessClassID"/>, <see cref="ClientCategoryID"/>, and
/// <see cref="ClientTypeID"/>.</summary>
public sealed class Client
{
    public int ClientID { get; set; }
    public string? ClientName { get; set; }
    public string? ClientShortName { get; set; }

    public int BusinessClassID { get; set; }
    public string? BusinessClassName { get; set; }
    public string? BusinessClassDescription { get; set; }
    public int? BusinessUnitID { get; set; }
    public string? BusinessUnitName { get; set; }

    public int ClientCategoryID { get; set; }
    public string? CCName { get; set; }

    public int ClientTypeID { get; set; }
    public string? CTName { get; set; }
    public string? CTDescription { get; set; }

    public int StatusID { get; set; }
    public string? StatusCode { get; set; }
    public string? StatusDescription { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedDate { get; set; }
    public DateTime? CreatedDate { get; set; }
    public DateTime? ModifiedDate { get; set; }

    public string DisplayName => string.IsNullOrWhiteSpace(ClientShortName)
        ? ClientName ?? $"Client {ClientID}"
        : $"{ClientName} ({ClientShortName})";
}
