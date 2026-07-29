using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using ClientManagementSystem.Models;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace ClientManagementSystem.ViewModels;

/// <summary>Shape returned to the New Project / Edit Project form. The CMS_PhaseI_All schema
/// requires <see cref="ProjectNumber"/> and <see cref="UGID"/> on every project (project-level
/// ownership has been removed from the schema). <c>AccountExecutive</c>/<c>AnalyticsLead</c>
/// free-text fields are retired, and the optional <see cref="RevenueAmount"/> entry writes a
/// row to <c>dbo.tProjectRevenue</c> rather than a column on <c>dbo.tProject</c>.</summary>
public sealed class ProjectFormViewModel
{
    public int? ProjectID { get; set; }

    /// <summary>Required, unique on <c>dbo.tProject</c>. Server-suggests a value on the
    /// New Project form.</summary>
    [Required]
    [StringLength(50)]
    [Display(Name = "Project Number")]
    public string ProjectNumber { get; set; } = string.Empty;

    [Required]
    [StringLength(200)]
    [Display(Name = "Project Name")]
    public string ProjectName { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Client")]
    public int ClientID { get; set; }

    [Display(Name = "User Group")]
    [JsonPropertyName("ugid")]
    public int UGID { get; set; }

    [Required]
    [Display(Name = "Project Type")]
    public int ProjectTypeID { get; set; }

    [Display(Name = "Renewal Date")]
    [DataType(DataType.Date)]
    public DateTime? RenewalDate { get; set; }

    [Display(Name = "Due Date")]
    [DataType(DataType.Date)]
    public DateTime? DueDate { get; set; }

    /// <summary>Status scoped to entity type 'Project' via <c>usp_Status_ListByEntity</c>.</summary>
    [Required]
    [Display(Name = "Status")]
    public int StatusID { get; set; }

    /// <summary>When true, the project is Online / currently in progress (green-dot on Home).</summary>
    [Display(Name = "Online")]
    public bool IsOnline { get; set; }

    /* ---- Revenue entry (writes a row to dbo.tProjectRevenue) ---- */

    [Display(Name = "Revenue")]
    [Range(0, double.MaxValue, ErrorMessage = "Revenue must be zero or greater.")]
    public decimal? RevenueAmount { get; set; }

    [Display(Name = "Effective Date")]
    [DataType(DataType.Date)]
    public DateTime? RevenueEffectiveDate { get; set; }

    [StringLength(500)]
    [Display(Name = "Reason")]
    public string? RevenueChangeReason { get; set; }

    /* ---- Read-only client profile (Client Info tab; sourced from dbo.vClient_WithBU) ---- */

    public string? ClientName { get; set; }
    public string? ClientShortName { get; set; }
    public int? ClientBusinessUnitID { get; set; }
    public string? ClientBusinessUnitName { get; set; }
    public int? ClientBusinessClassID { get; set; }
    public string? ClientBusinessClassName { get; set; }
    public int? ClientCategoryID { get; set; }
    public string? ClientCategoryName { get; set; }
    public int? ClientTypeID { get; set; }
    public string? ClientTypeName { get; set; }
    public int? ClientStatusID { get; set; }
    public string? ClientStatusCode { get; set; }

    /// <summary>Selected <c>dbo.tService.ServiceID</c> values for this project.</summary>
    public List<int> SelectedServiceIds { get; set; } = [];

    public IReadOnlyList<SelectListItem> ClientOptions { get; set; } = [];
    public IReadOnlyList<ClientListRow> ClientDirectoryRows { get; set; } = [];
    public IReadOnlyList<SelectListItem> BusinessUnitOptions { get; set; } = [];
    public IReadOnlyList<SelectListItem> BusinessClassOptions { get; set; } = [];
    public IReadOnlyList<SelectListItem> ClientCategoryOptions { get; set; } = [];
    public IReadOnlyList<SelectListItem> ClientTypeOptions { get; set; } = [];
    /// <summary>Client lifecycle statuses (entity type Client) for the Client Info tab.</summary>
    public IReadOnlyList<SelectListItem> ClientStatusOptions { get; set; } = [];
    public IReadOnlyList<SelectListItem> UserGroupOptions { get; set; } = [];
    public IReadOnlyList<SelectListItem> ProjectTypeOptions { get; set; } = [];
    public IReadOnlyList<SelectListItem> StatusOptions { get; set; } = [];
}
