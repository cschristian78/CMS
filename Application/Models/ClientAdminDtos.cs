using System.ComponentModel.DataAnnotations;

namespace ClientManagementSystem.Models;

/// <summary>One row for the Admin → Clients directory. Combines <c>dbo.tClient</c> (core + AE/Analytics
/// lead) with the primary <c>dbo.tClientRef</c> row that carries Business Unit / Business Class / Client
/// Type. Names are resolved server-side so the UI can show them directly.</summary>
public sealed class ClientAdminRow
{
    public int ClientID { get; set; }
    public string? ClientName { get; set; }
    public string? ClientShortName { get; set; }
    public string? ClientDescription { get; set; }
    public bool IsActive { get; set; }

    /// <summary>Lifecycle status: Active / Inactive / Prospect. Source of truth for the
    /// client's state; <see cref="IsActive"/> is kept in sync (Inactive => false).</summary>
    public string ClientStatus { get; set; } = "Active";

    public int? AccountExecutiveUserID { get; set; }
    public string? AccountExecutiveName { get; set; }
    public int? AnalyticsLeadUserID { get; set; }
    public string? AnalyticsLeadName { get; set; }

    public int? ClientRefID { get; set; }
    public int? BusinessUnitID { get; set; }
    public string? BusinessUnitName { get; set; }
    public int? BusinessClassID { get; set; }
    public string? BusinessClassName { get; set; }
    public int? ClientTypeID { get; set; }
    public string? ClientTypeName { get; set; }

    /// <summary>Status of the primary <c>dbo.tClientRef</c> row (joined to <c>dbo.tStatus</c>).
    /// Null when the client has no classification row yet, in which case the UI flags it as
    /// "Need Update".</summary>
    public int? RefStatusID { get; set; }
    public string? RefStatusCode { get; set; }
    public string? RefStatusDescription { get; set; }
}

/// <summary>Payload from the Admin Clients form. Business Unit / Business Class / Client Type are written
/// together as one <c>dbo.tClientRef</c> row (all three are required by that table).</summary>
public sealed class ClientAdminSaveRequest
{
    public int ClientID { get; set; }

    [MaxLength(150)]
    public string ClientName { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? ClientShortName { get; set; }

    [MaxLength(500)]
    public string? ClientDescription { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Active / Inactive / Prospect. When supplied this drives the stored
    /// <c>IsActive</c> flag (Inactive => 0, otherwise 1).</summary>
    [MaxLength(10)]
    public string ClientStatus { get; set; } = "Active";

    public int? AccountExecutiveUserID { get; set; }
    public int? AnalyticsLeadUserID { get; set; }

    public int? BusinessUnitID { get; set; }
    public int? BusinessClassID { get; set; }
    public int? ClientTypeID { get; set; }
}

/// <summary>One parsed CSV row for the Admin Clients bulk import. Business Unit / Business Class / Client
/// Type and the Account Executive / Analytics Lead are entered as friendly names / emails and resolved to
/// IDs during verify/commit.</summary>
public sealed class ClientAdminImportRowDto
{
    public int LineNumber { get; set; }

    [MaxLength(150)]
    public string ClientName { get; set; } = string.Empty;

    [MaxLength(50)]
    public string ClientShortName { get; set; } = string.Empty;

    [MaxLength(500)]
    public string ClientDescription { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public string AccountExecutiveEmail { get; set; } = string.Empty;
    public string AnalyticsLeadEmail { get; set; } = string.Empty;

    public string BusinessUnit { get; set; } = string.Empty;
    public string BusinessClass { get; set; } = string.Empty;
    public string ClientType { get; set; } = string.Empty;
}

public sealed class ClientAdminBulkParseResult
{
    public IReadOnlyList<ClientAdminImportRowDto> Rows { get; set; } = [];
    public int SkippedEmptyRows { get; set; }
}

public sealed class ClientAdminBulkVerifyResult
{
    public bool Valid { get; set; }
    public IReadOnlyList<string> Errors { get; set; } = [];
}

public sealed class ClientAdminBulkRowsRequest
{
    public IReadOnlyList<ClientAdminImportRowDto> Rows { get; set; } = [];
}

public sealed class ClientAdminBulkImportResult
{
    public int ImportedCount { get; set; }
    public int SkippedEmptyRows { get; set; }
    public IReadOnlyList<string> RowErrors { get; set; } = [];
}
