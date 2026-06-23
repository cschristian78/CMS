using System.ComponentModel.DataAnnotations;
using ClientManagementSystem.Models;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace ClientManagementSystem.ViewModels;

/// <summary>Shape returned to the New Client / Edit Client form. In the CMS_PhaseI_All schema
/// <c>tClient</c> carries direct FKs to <c>BusinessClassID</c>, <c>ClientCategoryID</c>, and
/// <c>ClientTypeID</c>, replacing the legacy link tables and Program lookup.</summary>
public sealed class ClientFormViewModel
{
    public int? ClientID { get; set; }

    [Required]
    [StringLength(200)]
    [Display(Name = "Client Name")]
    public string ClientName { get; set; } = string.Empty;

    [StringLength(50)]
    [Display(Name = "Short Name")]
    public string? ClientShortName { get; set; }

    [Required]
    [Display(Name = "Business Unit")]
    public int BusinessUnitID { get; set; }

    [Required]
    [Display(Name = "Business Class")]
    public int BusinessClassID { get; set; }

    [Required]
    [Display(Name = "Client Category")]
    public int ClientCategoryID { get; set; }

    [Required]
    [Display(Name = "Client Type")]
    public int ClientTypeID { get; set; }

    [Required]
    [Display(Name = "Status")]
    public int StatusID { get; set; }

    public bool IsDeleted { get; set; }

    public IReadOnlyList<ClientListRow> ExistingClients { get; set; } = [];

    /// <summary>Statuses scoped to entity type 'Client' via <c>usp_Status_ListByEntity</c>.</summary>
    public IReadOnlyList<SelectListItem> StatusOptions { get; set; } = [];
    public IReadOnlyList<SelectListItem> BusinessUnitOptions { get; set; } = [];
    public IReadOnlyList<SelectListItem> BusinessClassOptions { get; set; } = [];
    public IReadOnlyList<SelectListItem> ClientCategoryOptions { get; set; } = [];
    public IReadOnlyList<SelectListItem> ClientTypeOptions { get; set; } = [];
}
