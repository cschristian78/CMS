using System.Text.Json.Serialization;

namespace ClientManagementSystem.Models;

public sealed class ClientImportRowDto
{
    public int LineNumber { get; set; }
    public string ClientName { get; set; } = string.Empty;
    public string ClientShortName { get; set; } = string.Empty;

    [JsonPropertyName("statusID")]
    public int StatusID { get; set; }

    /// <summary>Resolved to <c>dbo.tBusinessClass.BusinessClassID</c> via the bulk-import resolver.</summary>
    [JsonPropertyName("businessClassID")]
    public int BusinessClassID { get; set; }

    [JsonPropertyName("clientCategoryID")]
    public int ClientCategoryID { get; set; }

    [JsonPropertyName("clientTypeID")]
    public int ClientTypeID { get; set; }
}

public sealed class ClientBulkParseResult
{
    public IReadOnlyList<ClientImportRowDto> Rows { get; set; } = [];
    public int SkippedEmptyRows { get; set; }
}

public sealed class ClientBulkVerifyResult
{
    public bool Valid { get; set; }
    public IReadOnlyList<string> Errors { get; set; } = [];
}

public sealed class ClientBulkRowsRequest
{
    public IReadOnlyList<ClientImportRowDto> Rows { get; set; } = [];
}
