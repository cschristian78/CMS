namespace ClientManagementSystem.Models;

/// <summary>
/// Row shape returned by <c>dbo.vClient_WithBU</c>. Replaces the retired
/// <c>dbo.usp_Ref_CreateClientList</c>/<c>dbo.tClientList</c> path.
/// </summary>
public sealed class ClientListRow
{
    public int ClientID { get; init; }
    public string? ClientName { get; init; }
    public string? ClientShortName { get; init; }

    public int? BusinessUnitID { get; init; }
    public string? BusinessUnitName { get; init; }

    public int? BusinessClassID { get; init; }
    public string? BusinessClassName { get; init; }
    public string? BusinessClassDescription { get; init; }

    public int? ClientCategoryID { get; init; }
    public string? CCName { get; init; }

    public int? ClientTypeID { get; init; }
    public string? CTName { get; init; }
    public string? CTDescription { get; init; }

    public int? StatusID { get; init; }
    public string? StatusCode { get; init; }
    public string? StatusDescription { get; init; }

    public DateTime? CreatedDate { get; init; }
    public DateTime? ModifiedDate { get; init; }
}
