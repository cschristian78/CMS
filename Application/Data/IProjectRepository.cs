using ClientManagementSystem.Models;

namespace ClientManagementSystem.Data;

public interface IProjectRepository
{
    /// <summary>Returns a candidate <c>ProjectNumber</c> for the New Project form. Equivalent to
    /// <c>MAX(ProjectID) + 1</c> formatted (the schema enforces uniqueness on
    /// <c>ProjectNumber</c>).</summary>
    Task<string> GetNextProjectNumberPreviewAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Project>> ListAsync(int? clientId = null, int? ugid = null, int? statusId = null, bool includeDeleted = false, CancellationToken cancellationToken = default);
    Task<Project?> GetAsync(int projectId, CancellationToken cancellationToken = default);
    Task<Project?> GetFullAsync(int projectId, CancellationToken cancellationToken = default);

    Task<int> AddAsync(Project project, CancellationToken cancellationToken = default);
    Task<int> UpdateAsync(Project project, CancellationToken cancellationToken = default);
    Task<int> SoftDeleteAsync(int projectId, CancellationToken cancellationToken = default);
    Task<int> RestoreAsync(int projectId, CancellationToken cancellationToken = default);

    // Revenue history
    Task<IReadOnlyList<ProjectRevenue>> GetRevenueHistoryAsync(int projectId, CancellationToken cancellationToken = default);
    Task<int> AddRevenueAsync(ProjectRevenue revenue, CancellationToken cancellationToken = default);
    Task<int> DeleteRevenueAsync(int revenueId, CancellationToken cancellationToken = default);

    // Services (project ↔ service junction)
    Task<IReadOnlyList<ProjectService>> ListServicesAsync(int projectId, CancellationToken cancellationToken = default);
    Task<int> AddServiceAsync(ProjectService link, CancellationToken cancellationToken = default);
    Task<int> RemoveServiceAsync(int projectId, int serviceId, CancellationToken cancellationToken = default);

    /// <summary>Saves the project header then replaces its <c>tProjectService</c> rows in one transaction.
    /// When <paramref name="revenueEntry"/> is supplied with a positive amount, also calls
    /// <c>dbo.usp_ProjectRevenue_Create</c> against the resolved <c>ProjectRefID</c>.</summary>
    Task<int> SaveProjectAndReplaceServicesAsync(
        Project project,
        IReadOnlyList<int> serviceIds,
        ProjectRevenue? revenueEntry = null,
        ProjectClientRefContext? clientRefContext = null,
        CancellationToken cancellationToken = default);

    // Notes
    Task<IReadOnlyList<ProjectNote>> ListNotesAsync(int projectId, CancellationToken cancellationToken = default);
    Task<int> AddNoteAsync(ProjectNote note, CancellationToken cancellationToken = default);
    Task<int> UpdateNoteAsync(ProjectNote note, CancellationToken cancellationToken = default);
    Task<int> DeleteNoteAsync(int noteId, CancellationToken cancellationToken = default);

    // Analytics Calendar
    Task<IReadOnlyList<ProjectCalendarItem>> ListCalendarItemsAsync(int projectId, CancellationToken cancellationToken = default);
    Task ReplaceCalendarItemsAsync(int projectId, IReadOnlyList<ProjectCalendarItem> items, CancellationToken cancellationToken = default);

    // Time tracker
    Task<IReadOnlyList<ProjectTimeEntry>> ListTimeEntriesForWeekAsync(
        int projectId,
        DateTime weekStartDate,
        int? userId = null,
        CancellationToken cancellationToken = default);
    Task SaveTimeEntriesForWeekAsync(
        int projectId,
        DateTime weekStartDate,
        IReadOnlyList<ProjectTimeEntry> entries,
        CancellationToken cancellationToken = default);

    // Project files (Azure Blob metadata)
    Task<IReadOnlyList<ProjectFile>> ListFilesAsync(int projectId, CancellationToken cancellationToken = default);
    Task<ProjectFile?> GetFileAsync(int projectFileId, CancellationToken cancellationToken = default);
    Task<int> AddFileAsync(ProjectFile file, CancellationToken cancellationToken = default);
    Task UpdateFileMetadataAsync(
        int projectFileId,
        string? description,
        string? versionLabel,
        DateTime? dataAsOf,
        CancellationToken cancellationToken = default);
    Task SetFileCurrentAsync(int projectFileId, bool isCurrent, CancellationToken cancellationToken = default);
    Task SoftDeleteFileAsync(int projectFileId, CancellationToken cancellationToken = default);

    /// <summary>Sets <c>dbo.tProject.IsOnline</c> without rewriting services or refs.</summary>
    Task SetOnlineAsync(int projectId, bool isOnline, CancellationToken cancellationToken = default);
}
