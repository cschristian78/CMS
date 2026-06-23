using Microsoft.AspNetCore.Mvc.Rendering;

namespace ClientManagementSystem.ViewModels;

/// <summary>Client Info tab dropdown options sourced from <c>dbo.tClientType</c> and
/// <c>dbo.tStatus</c> (client lifecycle statuses).</summary>
public sealed class ProjectClientClassificationLookups
{
    public IReadOnlyList<SelectListItem> ClientTypeOptions { get; set; } = [];
    public IReadOnlyList<SelectListItem> ClientStatusOptions { get; set; } = [];
}
