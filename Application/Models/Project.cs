namespace ClientManagementSystem.Models;

/// <summary>Mirrors <c>dbo.tProject</c> + <c>dbo.vProject_Current</c>. In the CMS_PhaseI_All
/// schema the project header carries <see cref="ProjectNumber"/> (unique), <see cref="UGID"/>
/// (FK to <c>dbo.tUserGroup</c>), and split <see cref="RenewalDate"/>/<see cref="DueDate"/>.
/// Project-level ownership has been removed from the schema. Revenue lives in the
/// <c>tProjectRevenue</c> history table; the latest entry is surfaced via
/// <see cref="LatestRevenue"/> from <c>vProject_Current</c>.</summary>
public sealed class Project
{
    public int ProjectID { get; set; }
    public string ProjectNumber { get; set; } = string.Empty;
    public string? ProjectName { get; set; }

    public int ClientID { get; set; }
    public string? ClientName { get; set; }
    public string? ClientShortName { get; set; }

    public int BusinessClassID { get; set; }
    public string? BusinessClassName { get; set; }
    public int? BusinessUnitID { get; set; }
    public string? BusinessUnitName { get; set; }

    public int UGID { get; set; }
    public string? UGName { get; set; }

    public int ProjectTypeID { get; set; }
    public string? PTCode { get; set; }
    public string? PTDescription { get; set; }

    public DateTime? RenewalDate { get; set; }
    public DateTime? DueDate { get; set; }

    public int StatusID { get; set; }
    public string? StatusCode { get; set; }
    public string? StatusDescription { get; set; }

    /// <summary>When true, the project is marked Online / currently in progress (green-dot on Home).</summary>
    public bool IsOnline { get; set; }

    /// <summary>When true, project revenue is fee-based (Project Info checkbox).</summary>
    public bool IsFeeBasedRevenue { get; set; }

    /// <summary>Latest <c>tProjectRevenue</c> row surfaced by <c>vProject_Current</c>.</summary>
    public decimal? LatestRevenue { get; set; }
    public DateTime? LatestRevenueEffectiveDate { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedDate { get; set; }
    public DateTime? CreatedDate { get; set; }
    public DateTime? ModifiedDate { get; set; }
}
