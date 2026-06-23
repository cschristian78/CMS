namespace ClientManagementSystem.ViewModels;

/// <summary>Active catalog row for the Project / Services tab (from <c>dbo.tService</c>).
/// <c>CategoryName</c> and <c>ServiceCategoryDescription</c> come from the joined
/// <c>dbo.tServiceCategory</c> row; tags come from <c>dbo.tServiceTag</c> → <c>dbo.tTag</c>.</summary>
public sealed class ProjectCatalogServiceRow
{
    public int ServiceID { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public string? ServiceDescription { get; set; }
    public string? CategoryName { get; set; }
    public string? ServiceCategoryDescription { get; set; }
    public int FunctionID { get; set; }
    public int ServiceCategoryID { get; set; }
    public IReadOnlyList<string> Tags { get; set; } = [];
    public int StatusID { get; set; }
    public string? StatusCode { get; set; }
}
