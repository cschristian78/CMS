namespace ClientManagementSystem.Models;

/// <summary>Mirrors <c>dbo.tServiceCategory</c> (<c>CategoryName</c> +
/// <c>ServiceCategoryDescription</c>).</summary>
public sealed class ServiceCategory
{
    public int ServiceCategoryID { get; set; }
    public string CategoryName { get; set; } = string.Empty;

    /// <summary>Optional grouping/description text stored on <c>dbo.tServiceCategory</c>.</summary>
    public int FunctionID { get; set; }

    /// <summary>Maps to <c>dbo.tServiceCategory.ServiceCategoryDescription</c>.</summary>
    public string? ServiceCategoryDescription { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? CreatedDate { get; set; }
    public DateTime? ModifiedDate { get; set; }

    public string DisplayName => string.IsNullOrWhiteSpace(ServiceCategoryDescription)
        ? CategoryName
        : $"{CategoryName} ({ServiceCategoryDescription})";
}
