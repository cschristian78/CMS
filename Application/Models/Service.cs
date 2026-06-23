using System.ComponentModel.DataAnnotations;

namespace ClientManagementSystem.Models;

/// <summary>Mirrors <c>dbo.tService</c> in the CMS_PhaseI_All schema. Drops inline
/// <c>CatFunction</c>/<c>ServiceCategory</c>/<c>Tag1-3</c>; adds <see cref="ServiceCategoryID"/>
/// FK and a many-to-many <see cref="Tags"/> collection via <c>dbo.tServiceTag</c>.</summary>
public sealed class Service
{
    public int ServiceID { get; set; }

    [Required]
    [MaxLength(150)]
    public string ServiceName { get; set; } = string.Empty;

    [MaxLength(255)]
    public string? ServiceDescription { get; set; }

    public int ServiceCategoryID { get; set; }

    /// <summary>Optional FK to <c>dbo.tServiceGroup</c> recorded on the service's <c>tServiceRef</c> row.</summary>
    public int? ServiceGroupID { get; set; }

    /// <summary>Resolved at read-time via <c>JOIN dbo.tServiceGroup</c>.</summary>
    public string? ServiceGroupName { get; set; }

    /// <summary>FK to <c>dbo.tCatFunction</c>; must match the parent row of <see cref="ServiceCategoryID"/>.</summary>
    public int FunctionID { get; set; }

    /// <summary>Resolved at read-time via <c>JOIN tServiceCategory</c>.</summary>
    public string? CategoryName { get; set; }

    /// <summary>Resolved at read-time from <c>dbo.tServiceCategory.ServiceCategoryDescription</c>.</summary>
    public string? ServiceCategoryDescription { get; set; }

    public int StatusID { get; set; }
    public string? StatusCode { get; set; }
    public string? StatusDescription { get; set; }

    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; }
    public DateTime? DeletedDate { get; set; }
    public DateTime? CreatedDate { get; set; }
    public DateTime? ModifiedDate { get; set; }

    /// <summary>Joined tag names from <c>dbo.tServiceTag</c> via <c>dbo.tTag</c>.</summary>
    public IReadOnlyList<Tag> Tags { get; set; } = [];

    public string DisplayName =>
        string.IsNullOrWhiteSpace(CategoryName)
            ? ServiceName
            : $"{ServiceName} ({CategoryName})";
}
