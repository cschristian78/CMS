using System.ComponentModel.DataAnnotations;

namespace ClientManagementSystem.Models;

/// <summary>One parsed CSV row for Home → client + project bulk import. Friendly names are resolved to
/// IDs during verify/commit (no IDs in the CSV).</summary>
public sealed class HomeClientProjectImportRowDto
{
    public int LineNumber { get; set; }

    [MaxLength(150)]
    public string ClientName { get; set; } = string.Empty;

    [MaxLength(50)]
    public string ClientShortName { get; set; } = string.Empty;

    [MaxLength(500)]
    public string ClientDescription { get; set; } = string.Empty;

    public string ClientStatus { get; set; } = "Active";

    public string AccountExecutiveEmail { get; set; } = string.Empty;
    public string AnalyticsLeadEmail { get; set; } = string.Empty;

    public string BusinessUnit { get; set; } = string.Empty;
    public string BusinessClass { get; set; } = string.Empty;
    public string ClientType { get; set; } = string.Empty;

    [MaxLength(50)]
    public string ProjectNumber { get; set; } = string.Empty;

    [MaxLength(200)]
    public string ProjectName { get; set; } = string.Empty;

    public string ProjectType { get; set; } = string.Empty;
    public string ProjectStatus { get; set; } = string.Empty;

    public string RenewalDate { get; set; } = string.Empty;
    public string DueDate { get; set; } = string.Empty;
    public string RevenueAmount { get; set; } = string.Empty;
    public string RevenueEffectiveDate { get; set; } = string.Empty;
}

public sealed class HomeClientProjectBulkParseResult
{
    public IReadOnlyList<HomeClientProjectImportRowDto> Rows { get; set; } = [];
    public int SkippedEmptyRows { get; set; }
}

public sealed class HomeClientProjectBulkVerifyResult
{
    public bool Valid { get; set; }
    public IReadOnlyList<string> Errors { get; set; } = [];
}

public sealed class HomeClientProjectBulkRowsRequest
{
    public IReadOnlyList<HomeClientProjectImportRowDto> Rows { get; set; } = [];
}

public sealed class HomeClientProjectBulkImportResult
{
    public int ImportedCount { get; set; }
    public int SkippedEmptyRows { get; set; }
    public IReadOnlyList<string> RowErrors { get; set; } = [];
}
