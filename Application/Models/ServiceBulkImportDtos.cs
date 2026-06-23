using System.Text.Json.Serialization;

namespace ClientManagementSystem.Models;

public sealed class ServiceImportRowDto
{
    [JsonPropertyName("serviceID")]
    public int ServiceID { get; set; }

    public int LineNumber { get; set; }

    /// <summary>Resolved to <c>dbo.tServiceCategory.ServiceCategoryID</c> when provided on import.</summary>
    [JsonPropertyName("categoryName")]
    public string CategoryName { get; set; } = string.Empty;

    /// <summary>Optional description stored on the resolved
    /// <c>dbo.tServiceCategory</c> row.</summary>
    [JsonPropertyName("serviceCategoryDescription")]
    public string ServiceCategoryDescription { get; set; } = string.Empty;

    /// <summary>Optional; resolved to <c>dbo.tServiceGroup.ServiceGroupID</c> on the
    /// <c>tServiceRef</c> row when provided.</summary>
    [JsonPropertyName("serviceGroupName")]
    public string ServiceGroupName { get; set; } = string.Empty;

    public string ServiceName { get; set; } = string.Empty;
    public string ServiceDescription { get; set; } = string.Empty;

    /// <summary>Optional semicolon- or comma-separated tag names. Each is resolved via
    /// <c>usp_Tag_GetOrCreate</c> (TagScope='Service') and attached through
    /// <c>usp_Service_AddTag</c>.</summary>
    [JsonPropertyName("tags")]
    public string Tags { get; set; } = string.Empty;

    [JsonPropertyName("statusID")]
    public int StatusID { get; set; }

    /// <summary>Optional; when null or omitted SQL uses <c>SYSUTCDATETIME()</c>.</summary>
    [JsonPropertyName("createdDate")]
    public DateTime? CreatedDate { get; set; }
}

public sealed class ServiceBulkParseResult
{
    public IReadOnlyList<ServiceImportRowDto> Rows { get; set; } = [];
    public int SkippedEmptyRows { get; set; }
}

public sealed class ServiceBulkVerifyResult
{
    public bool Valid { get; set; }
    public IReadOnlyList<string> Errors { get; set; } = [];
}

public sealed class ServiceBulkRowsRequest
{
    public IReadOnlyList<ServiceImportRowDto> Rows { get; set; } = [];
}

public sealed class ServiceBulkImportResult
{
    public int ImportedCount { get; set; }
    public int SkippedEmptyRows { get; set; }
    public IReadOnlyList<string> RowErrors { get; set; } = [];
}
