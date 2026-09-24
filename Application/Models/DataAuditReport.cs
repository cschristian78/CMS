namespace ClientManagementSystem.Models;

/// <summary>Admin Data Audit report for Client → Project → User Group → User linkage gaps.</summary>
public sealed class DataAuditReport
{
    public DateTimeOffset GeneratedAtUtc { get; set; }
    public IReadOnlyList<DataAuditCheck> Checks { get; set; } = [];
}

public sealed class DataAuditCheck
{
    public string CheckId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Count { get; set; }
    public IReadOnlyList<string> Columns { get; set; } = [];
    public IReadOnlyList<IReadOnlyList<string?>> Rows { get; set; } = [];
}

/// <summary>Catalog entry for a Data Audit check (no findings).</summary>
public sealed class DataAuditCheckDescriptor
{
    public string CheckId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    /// <summary>CatAI tool function name for this check.</summary>
    public string ToolName { get; set; } = string.Empty;
}


/// <summary>PDF/export payload: section summaries plus findings rolled up by Analytics Lead.</summary>
public sealed class DataAuditExportReport
{
    public DateTimeOffset GeneratedAtUtc { get; set; }
    public IReadOnlyList<DataAuditExportSection> Sections { get; set; } = [];
    public IReadOnlyList<DataAuditExportLeadGroup> ByAnalyticsLead { get; set; } = [];
}

public sealed class DataAuditExportSection
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int FindingTotal { get; set; }
    public IReadOnlyList<DataAuditExportCheckSummary> Checks { get; set; } = [];
}

public sealed class DataAuditExportCheckSummary
{
    public string CheckId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Count { get; set; }
}

public sealed class DataAuditExportLeadGroup
{
    public int? AnalyticsLeadUserID { get; set; }
    public string AnalyticsLeadName { get; set; } = "Unassigned";
    public string? AnalyticsLeadEmail { get; set; }
    public int FindingTotal { get; set; }
    public IReadOnlyList<DataAuditExportFinding> Findings { get; set; } = [];
}

public sealed class DataAuditExportFinding
{
    public string SectionKey { get; set; } = string.Empty;
    public string SectionLabel { get; set; } = string.Empty;
    public string CheckId { get; set; } = string.Empty;
    public string CheckTitle { get; set; } = string.Empty;
    public string EntityLabel { get; set; } = string.Empty;
}
