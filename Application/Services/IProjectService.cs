using ClientManagementSystem.Models;
using ClientManagementSystem.ViewModels;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace ClientManagementSystem.Services;

public interface IProjectService
{
    Task<ProjectFormViewModel> PrepareCreateFormAsync(ProjectFormViewModel? model = null, CancellationToken cancellationToken = default);
    Task<ProjectFormViewModel?> PrepareEditFormAsync(int projectId, CancellationToken cancellationToken = default);

    /// <summary>Client Info tab options from <c>dbo.tClientType</c> and <c>dbo.tStatus</c>.</summary>
    Task<ProjectClientClassificationLookups> GetClientClassificationLookupsAsync(CancellationToken cancellationToken = default);

    /// <summary>Saves the project header and replaces <c>tProjectService</c> rows. If
    /// <see cref="ProjectFormViewModel.RevenueAmount"/> is set, writes a row to
    /// <c>tProjectRevenue</c> in the same call.</summary>
    Task<int> SaveAsync(ProjectFormViewModel model, CancellationToken cancellationToken = default);

    Task SoftDeleteAsync(int projectId, CancellationToken cancellationToken = default);
    Task RestoreAsync(int projectId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SelectListItem>> GetUserGroupSelectListForClientAsync(int clientId, CancellationToken cancellationToken = default);

    /// <summary>Active services from <c>dbo.tService</c> joined to <c>dbo.tServiceCategory</c>
    /// (and tags from <c>dbo.tServiceTag</c>), for the Project / Services tab.</summary>
    Task<IReadOnlyList<ProjectCatalogServiceRow>> GetActiveServicesCatalogAsync(CancellationToken cancellationToken = default);

    // Sub-resource accessors (Revenue history, Notes, Activity) live on the project repository
    // and are exposed via the API controller directly.
    Task<IReadOnlyList<ProjectRevenue>> GetRevenueHistoryAsync(int projectId, CancellationToken cancellationToken = default);
    Task<int> AddRevenueAsync(ProjectRevenue revenue, CancellationToken cancellationToken = default);
    Task<int> DeleteRevenueAsync(int revenueId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProjectNote>> ListNotesAsync(int projectId, CancellationToken cancellationToken = default);
    Task<int> AddNoteAsync(ProjectNote note, CancellationToken cancellationToken = default);
    Task<int> UpdateNoteAsync(ProjectNote note, CancellationToken cancellationToken = default);
    Task<int> DeleteNoteAsync(int noteId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ActivityLog>> GetActivityAsync(int projectId, int top = 100, CancellationToken cancellationToken = default);
}
