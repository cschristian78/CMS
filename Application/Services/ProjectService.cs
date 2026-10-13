using System.Globalization;
using ClientManagementSystem.Data;
using ClientManagementSystem.Models;
using ClientManagementSystem.ViewModels;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Logging;

namespace ClientManagementSystem.Services;

/// <summary>Form preparation and persistence for projects against the CMS_PhaseI_All schema.
/// In the new schema every project requires <c>ProjectNumber</c> (unique) and <c>UGID</c>
/// (project-level ownership has been removed from the schema). Revenue lives in the
/// <c>tProjectRevenue</c> history table.</summary>
public sealed class ProjectService : IProjectService
{
    private const string ProjectEntityType = "Project";
    private const string ClientEntityType = "Client";

    private readonly IProjectRepository _projects;
    private readonly IClientRepository _clients;
    private readonly IReferenceDataRepository _referenceData;
    private readonly IAdminRepository _admin;
    private readonly IDashboardRepository _dashboard;
    private readonly IProjectBlobStorage _blobs;
    private readonly ICalendarOwnerNotificationService _calendarOwnerNotifications;
    private readonly IFileNoteNotificationService _fileNoteNotifications;
    private readonly IRealtimeNotifier _realtime;
    private readonly ILogger<ProjectService>? _logger;

    public ProjectService(
        IProjectRepository projects,
        IClientRepository clients,
        IReferenceDataRepository referenceData,
        IAdminRepository admin,
        IDashboardRepository dashboard,
        IProjectBlobStorage blobs,
        ICalendarOwnerNotificationService calendarOwnerNotifications,
        IFileNoteNotificationService fileNoteNotifications,
        IRealtimeNotifier realtime,
        ILogger<ProjectService>? logger = null)
    {
        _projects = projects;
        _clients = clients;
        _referenceData = referenceData;
        _admin = admin;
        _dashboard = dashboard;
        _blobs = blobs;
        _calendarOwnerNotifications = calendarOwnerNotifications;
        _fileNoteNotifications = fileNoteNotifications;
        _realtime = realtime;
        _logger = logger;
    }

    private async Task RefreshHomeListAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _dashboard.RefreshClientProjectListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "dbo.usp_Create_vProject_Current refresh failed after a Project write.");
        }
    }

    public async Task<ProjectFormViewModel> PrepareCreateFormAsync(ProjectFormViewModel? model = null, CancellationToken cancellationToken = default)
    {
        model ??= new ProjectFormViewModel();
        model.SelectedServiceIds ??= [];

        if (string.IsNullOrWhiteSpace(model.ProjectNumber))
        {
            model.ProjectNumber = await _projects.GetNextProjectNumberPreviewAsync(cancellationToken);
        }

        return await PopulateListsAsync(model, cancellationToken);
    }

    public async Task<ProjectClientClassificationLookups> GetClientClassificationLookupsAsync(
        CancellationToken cancellationToken = default)
    {
        var clientTypes = await _referenceData.GetClientTypesAsync(activeOnly: false, cancellationToken);
        var clientStatuses = await _referenceData.GetStatusesForEntityAsync(ClientEntityType, cancellationToken);

        return new ProjectClientClassificationLookups
        {
            ClientTypeOptions = clientTypes
                .OrderBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(c => new SelectListItem(c.DisplayName, c.ClientTypeID.ToString(CultureInfo.InvariantCulture)))
                .ToList(),
            ClientStatusOptions = clientStatuses
                .OrderBy(s => s.StatusID)
                .Select(s => new SelectListItem(s.DisplayName, s.StatusID.ToString(CultureInfo.InvariantCulture)))
                .ToList()
        };
    }

    public async Task<ProjectFormViewModel?> PrepareEditFormAsync(int projectId, CancellationToken cancellationToken = default)
    {
        var project = await _projects.GetFullAsync(projectId, cancellationToken) ?? await _projects.GetAsync(projectId, cancellationToken);
        if (project is null)
        {
            return null;
        }

        var model = new ProjectFormViewModel
        {
            ProjectID = project.ProjectID,
            ProjectNumber = project.ProjectNumber,
            ProjectName = project.ProjectName ?? string.Empty,
            ClientID = project.ClientID,
            UGID = project.UGID,
            ProjectTypeID = project.ProjectTypeID,
            RenewalDate = project.RenewalDate,
            DueDate = project.DueDate,
            StatusID = project.StatusID,
            IsOnline = project.IsOnline,
            IsFeeBasedRevenue = project.IsFeeBasedRevenue,
            ClientBusinessUnitID = project.BusinessUnitID,
            ClientBusinessUnitName = project.BusinessUnitName,
            ClientBusinessClassID = project.BusinessClassID > 0 ? project.BusinessClassID : null,
            ClientBusinessClassName = project.BusinessClassName,
        };

        var client = await _clients.GetAsync(project.ClientID, cancellationToken);
        if (client is not null)
        {
            model.ClientName = client.ClientName;
            model.ClientShortName = client.ClientShortName;
            if (model.ClientBusinessUnitID is null or <= 0)
            {
                model.ClientBusinessUnitID = client.BusinessUnitID;
                model.ClientBusinessUnitName = client.BusinessUnitName;
            }
            if (model.ClientBusinessClassID is null or <= 0)
            {
                model.ClientBusinessClassID = client.BusinessClassID;
                model.ClientBusinessClassName = BusinessClass.FormatDisplayDescription(
                    client.BusinessClassName,
                    client.BusinessClassDescription);
            }
            if (model.ClientCategoryID is null or <= 0)
            {
                model.ClientCategoryID = client.ClientCategoryID;
                model.ClientCategoryName = client.CCName;
            }
            if (model.ClientTypeID is null or <= 0)
            {
                model.ClientTypeID = client.ClientTypeID;
                model.ClientTypeName = ClientType.FormatDisplayName(client.CTName, client.CTDescription);
            }
            if (model.ClientStatusID is null or <= 0)
            {
                model.ClientStatusID = client.StatusID;
                model.ClientStatusCode = client.StatusCode;
            }
        }

        var services = await _projects.ListServicesAsync(projectId, cancellationToken);
        model.SelectedServiceIds = services.Select(s => s.ServiceID).ToList();

        var revenueHistory = await _projects.GetRevenueHistoryAsync(projectId, cancellationToken);
        if (revenueHistory.Count > 0)
        {
            var latest = revenueHistory[0];
            model.RevenueAmount = latest.Revenue;
            model.RevenueEffectiveDate = latest.EffectiveDate;
            model.RevenueChangeReason = latest.ChangeReason;
        }

        return await PopulateListsAsync(model, cancellationToken);
    }

    public async Task<int> SaveAsync(ProjectFormViewModel model, CancellationToken cancellationToken = default)
    {
        model.SelectedServiceIds ??= [];

        var project = new Project
        {
            ProjectID = model.ProjectID ?? 0,
            ProjectNumber = (model.ProjectNumber ?? string.Empty).Trim(),
            ProjectName = (model.ProjectName ?? string.Empty).Trim(),
            ClientID = model.ClientID,
            UGID = model.UserGroupID > 0 ? model.UserGroupID : 0,
            ProjectTypeID = model.ProjectTypeID,
            RenewalDate = model.RenewalDate,
            DueDate = model.DueDate,
            StatusID = model.StatusID > 0 ? model.StatusID : 1,
            IsOnline = model.IsOnline,
            IsFeeBasedRevenue = model.IsFeeBasedRevenue
        };

        var existing = project.ProjectID > 0
            ? await _projects.GetAsync(project.ProjectID, cancellationToken)
            : null;

        ProjectRevenue? revenueEntry = null;
        if (model.RevenueAmount is > 0m)
        {
            revenueEntry = new ProjectRevenue
            {
                ProjectID = project.ProjectID,
                EffectiveDate = model.RevenueEffectiveDate?.Date ?? DateTime.UtcNow.Date,
                Revenue = model.RevenueAmount.Value,
                ChangeReason = model.RevenueChangeReason,
                RecordedBy = CmsDefaults.ActingUserId
            };
        }

        var clientRefContext = new ProjectClientRefContext(
            BusinessUnitId: model.ClientBusinessUnitID,
            BusinessClassId: model.ClientBusinessClassID,
            ClientTypeId: model.ClientTypeID,
            ClientStatusId: model.ClientStatusID);

        var projectId = await _projects.SaveProjectAndReplaceServicesAsync(
            project,
            model.SelectedServiceIds,
            revenueEntry,
            clientRefContext,
            cancellationToken);

        model.CalendarItems ??= [];
        try
        {
            await _projects.ReplaceCalendarItemsAsync(projectId, model.CalendarItems, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Calendar replace failed for ProjectID {ProjectID} ({Count} items).", projectId, model.CalendarItems.Count);
            throw;
        }

        // Explicit ActivityLog: status transitions and creation.
        try
        {
            if (existing is null)
            {
                await _admin.LogActivityAsync(ProjectEntityType, projectId, "Add", null, null, project.ProjectName, cancellationToken);
            }
            else if (existing.StatusID != project.StatusID)
            {
                await _admin.LogActivityAsync(
                    ProjectEntityType,
                    projectId,
                    "StatusChange",
                    nameof(Project.StatusID),
                    existing.StatusID.ToString(CultureInfo.InvariantCulture),
                    project.StatusID.ToString(CultureInfo.InvariantCulture),
                    cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Activity log write failed for ProjectID {ProjectID}.", projectId);
        }

        await RefreshHomeListAsync(cancellationToken);
        return projectId;
    }

    public async Task SoftDeleteAsync(int projectId, CancellationToken cancellationToken = default)
    {
        await _projects.SoftDeleteAsync(projectId, cancellationToken);
        try
        {
            await _admin.LogActivityAsync(ProjectEntityType, projectId, "SoftDelete", null, null, null, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Activity log write failed for SoftDelete of ProjectID {ProjectID}.", projectId);
        }

        await RefreshHomeListAsync(cancellationToken);
    }

    public async Task<ProjectHardDeleteResult> HardDeleteAsync(int projectId, CancellationToken cancellationToken = default)
    {
        var result = await _projects.HardDeleteAsync(projectId, cancellationToken);

        foreach (var blobPath in result.BlobPaths)
        {
            try
            {
                await _blobs.DeleteAsync(blobPath, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(
                    ex,
                    "Hard-deleted ProjectID {ProjectID} but blob delete failed for {BlobPath}.",
                    projectId,
                    blobPath);
            }
        }

        await RefreshHomeListAsync(cancellationToken);
        return result;
    }

    public async Task SetOnlineAsync(int projectId, bool isOnline, CancellationToken cancellationToken = default)
    {
        await _projects.SetOnlineAsync(projectId, isOnline, cancellationToken);
        try
        {
            await _admin.LogActivityAsync(
                ProjectEntityType,
                projectId,
                "OnlineChange",
                nameof(Project.IsOnline),
                (!isOnline).ToString(),
                isOnline.ToString(),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Activity log write failed for OnlineChange of ProjectID {ProjectID}.", projectId);
        }

        await RefreshHomeListAsync(cancellationToken);
    }

    public async Task RestoreAsync(int projectId, CancellationToken cancellationToken = default)
    {
        await _projects.RestoreAsync(projectId, cancellationToken);
        try
        {
            await _admin.LogActivityAsync(ProjectEntityType, projectId, "Restore", null, null, null, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Activity log write failed for Restore of ProjectID {ProjectID}.", projectId);
        }

        await RefreshHomeListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SelectListItem>> GetUserGroupSelectListForClientAsync(
        int clientId,
        CancellationToken cancellationToken = default)
    {
        // Always offer every active user group so Edit/New can assign any group.
        // Optionally put groups already used on this client's projects first.
        var all = await _referenceData.GetTopUserGroupSummariesAsync(null, cancellationToken);
        var usedIds = clientId > 0
            ? (await _referenceData.GetUserGroupsUsedOnClientProjectsAsync(clientId, cancellationToken))
                .Select(g => g.UGID)
                .ToHashSet()
            : new HashSet<int>();

        return all
            .OrderByDescending(g => usedIds.Contains(g.UGID))
            .ThenBy(g => g.UGName ?? g.UserGroupNumber, StringComparer.OrdinalIgnoreCase)
            .ThenBy(g => g.UGID)
            .Select(g =>
            {
                var label = string.IsNullOrWhiteSpace(g.UGName)
                    ? $"Group {g.UserGroupNumber}"
                    : $"{g.UGName} ({g.UserGroupNumber})";
                return new SelectListItem(label, g.UGID.ToString(CultureInfo.InvariantCulture));
            })
            .ToList();
    }

    public async Task<IReadOnlyList<ProjectCatalogServiceRow>> GetActiveServicesCatalogAsync(CancellationToken cancellationToken = default)
    {
        var services = await _admin.GetServicesAsync(includeDeleted: false, cancellationToken);
        return services
            .Where(s => !s.IsDeleted)
            .OrderBy(s => s.CategoryName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.ServiceName, StringComparer.OrdinalIgnoreCase)
            .Select(s => new ProjectCatalogServiceRow
            {
                ServiceID = s.ServiceID,
                ServiceName = s.ServiceName,
                ServiceDescription = s.ServiceDescription,
                CategoryName = s.CategoryName,
                ServiceCategoryDescription = s.ServiceCategoryDescription,
                FunctionID = s.FunctionID,
                ServiceCategoryID = s.ServiceCategoryID,
                ServiceGroupID = s.ServiceGroupID,
                ServiceGroupName = s.ServiceGroupName,
                Tags = s.Tags.Select(t => t.TagName).ToList(),
                StatusID = s.StatusID,
                StatusCode = s.StatusCode
            })
            .ToList();
    }

    public Task<IReadOnlyList<ProjectRevenue>> GetRevenueHistoryAsync(int projectId, CancellationToken cancellationToken = default)
        => _projects.GetRevenueHistoryAsync(projectId, cancellationToken);

    public Task<int> AddRevenueAsync(ProjectRevenue revenue, CancellationToken cancellationToken = default)
        => _projects.AddRevenueAsync(revenue, cancellationToken);

    public Task<int> DeleteRevenueAsync(int revenueId, CancellationToken cancellationToken = default)
        => _projects.DeleteRevenueAsync(revenueId, cancellationToken);

    public Task<IReadOnlyList<ProjectNote>> ListNotesAsync(int projectId, CancellationToken cancellationToken = default)
        => _projects.ListNotesAsync(projectId, cancellationToken);

    public async Task<int> AddNoteAsync(ProjectNote note, int actingUserId = 0, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(note);

        if (actingUserId > 0)
        {
            note.UserID = actingUserId;
        }

        var isFileNote = note.ProjectFileID is > 0;
        if (isFileNote)
        {
            note.NoteType = FileNoteKinds.Normalize(note.NoteType);
        }

        var noteId = await _projects.AddNoteAsync(note, cancellationToken);
        note.NoteID = noteId;

        if (!isFileNote)
        {
            return noteId;
        }

        await _realtime.NotifyProjectAsync(
            new RealtimeUpdate
            {
                Kind = "projectNote",
                ProjectId = note.ProjectID,
                ChangeType = "created",
                NoteId = noteId,
                ProjectFileId = note.ProjectFileID,
            },
            cancellationToken);

        var project = await _projects.GetAsync(note.ProjectID, cancellationToken);
        var file = await _projects.GetFileAsync(note.ProjectFileID!.Value, cancellationToken);
        if (project is null || file is null || file.ProjectID != note.ProjectID)
        {
            return noteId;
        }

        var approvalStatus = FileNoteKinds.ApprovalStatusForKind(note.NoteType);
        if (approvalStatus is not null)
        {
            try
            {
                await _projects.UpdateFileApprovalStatusAsync(
                    file.ProjectFileID,
                    approvalStatus,
                    note.UserID,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(
                    ex,
                    "Could not update approval status for ProjectFileID {FileId} after note {NoteId}.",
                    file.ProjectFileID,
                    noteId);
            }
        }

        var mentionIds = ParseMentionedUserIds(note);
        if (note.NotifyCatTeam || note.NotifyAll || mentionIds.Count > 0)
        {
            var authorName = note.UserFullName;
            if (string.IsNullOrWhiteSpace(authorName) && note.UserID > 0)
            {
                var contacts = await _admin.GetUserContactInfoByIdsAsync([note.UserID], cancellationToken);
                authorName = contacts.FirstOrDefault()?.FullName;
            }

            await _fileNoteNotifications.TryNotifyAsync(
                project,
                file,
                note,
                authorName ?? string.Empty,
                note.NotifyCatTeam,
                note.NotifyAll,
                mentionIds,
                cancellationToken);
        }

        return noteId;
    }

    private static List<int> ParseMentionedUserIds(ProjectNote note)
    {
        var ids = new HashSet<int>();
        if (note.MentionedUserIdList is { Length: > 0 })
        {
            foreach (var id in note.MentionedUserIdList)
            {
                if (id > 0)
                {
                    ids.Add(id);
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(note.MentionedUserIDs))
        {
            foreach (var part in note.MentionedUserIDs.Split(
                         ',',
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) && id > 0)
                {
                    ids.Add(id);
                }
            }
        }

        return ids.ToList();
    }

    public async Task<int> UpdateNoteAsync(ProjectNote note, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(note);
        var context = await _projects.GetNoteContextAsync(note.NoteID, cancellationToken);
        var updated = await _projects.UpdateNoteAsync(note, cancellationToken);
        if (context is { } ctx)
        {
            await _realtime.NotifyProjectAsync(
                new RealtimeUpdate
                {
                    Kind = "projectNote",
                    ProjectId = ctx.ProjectId,
                    ChangeType = "updated",
                    NoteId = note.NoteID,
                    ProjectFileId = ctx.ProjectFileId,
                },
                cancellationToken);
        }

        return updated;
    }

    public async Task<int> DeleteNoteAsync(int noteId, CancellationToken cancellationToken = default)
    {
        var context = await _projects.GetNoteContextAsync(noteId, cancellationToken);
        var deleted = await _projects.DeleteNoteAsync(noteId, cancellationToken);
        if (context is { } ctx)
        {
            await _realtime.NotifyProjectAsync(
                new RealtimeUpdate
                {
                    Kind = "projectNote",
                    ProjectId = ctx.ProjectId,
                    ChangeType = "deleted",
                    NoteId = noteId,
                    ProjectFileId = ctx.ProjectFileId,
                },
                cancellationToken);
        }

        return deleted;
    }

    public Task<IReadOnlyList<ProjectCalendarItem>> ListCalendarItemsAsync(
        int projectId,
        CancellationToken cancellationToken = default)
        => _projects.ListCalendarItemsAsync(projectId, cancellationToken);

    public async Task<int> SaveCalendarItemsAsync(
        int projectId,
        IReadOnlyList<ProjectCalendarItem> items,
        int actingUserId = 0,
        CancellationToken cancellationToken = default)
    {
        if (projectId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(projectId));
        }

        var existing = await _projects.GetAsync(projectId, cancellationToken);
        if (existing is null)
        {
            throw new InvalidOperationException($"Project {projectId} was not found.");
        }

        items ??= Array.Empty<ProjectCalendarItem>();
        var beforeSave = await _projects.ListCalendarItemsAsync(projectId, cancellationToken);

        // Calendar Notes cell is a one-shot draft: append to tProjectNote history, then clear the cell.
        var noteSources = items
            .Select((item, index) => new
            {
                item.CalendarItemID,
                Title = (item.Title ?? string.Empty).Trim(),
                Notes = string.IsNullOrWhiteSpace(item.Notes) ? null : item.Notes.Trim(),
                SortOrder = item.SortOrder >= 0 ? item.SortOrder : index,
            })
            .Where(x => x.Title.Length > 0 && x.Notes is not null)
            .ToList();

        var itemsToSave = items
            .Select(item => new ProjectCalendarItem
            {
                CalendarItemID = item.CalendarItemID,
                ProjectID = item.ProjectID,
                ProjectRefID = item.ProjectRefID,
                ItemType = item.ItemType,
                Title = item.Title,
                OwnerUserID = item.OwnerUserID,
                OwnerFullName = item.OwnerFullName,
                ReceivedDate = item.ReceivedDate,
                CompletedDate = item.CompletedDate,
                PercentComplete = item.PercentComplete,
                Notes = null,
                SortOrder = item.SortOrder,
                IsActive = item.IsActive,
                CreatedDate = item.CreatedDate,
                ModifiedDate = item.ModifiedDate,
            })
            .ToList();

        await _projects.ReplaceCalendarItemsAsync(projectId, itemsToSave, cancellationToken);
        var afterSave = await _projects.ListCalendarItemsAsync(projectId, cancellationToken);

        if (noteSources.Count > 0)
        {
            var userId = actingUserId > 0 ? actingUserId : CmsDefaults.ActingUserId;
            var usedIds = new HashSet<int>();

            foreach (var src in noteSources)
            {
                var calendarItemId = ResolveCalendarItemId(src.CalendarItemID, src.Title, src.SortOrder, afterSave, usedIds);
                await _projects.AddNoteAsync(
                    new ProjectNote
                    {
                        ProjectID = projectId,
                        UserID = userId,
                        NoteDate = DateTime.UtcNow,
                        NoteType = "Calendar",
                        NoteText = src.Notes!,
                        IsPinned = false,
                        CalendarItemID = calendarItemId > 0 ? calendarItemId : null,
                        CalendarItemTitle = src.Title,
                    },
                    cancellationToken);
            }
        }

        var saved = items.Count(i => !string.IsNullOrWhiteSpace(i.Title));
        _logger?.LogInformation("Saved {Count} calendar item(s) for ProjectID {ProjectID}.", saved, projectId);

        await _calendarOwnerNotifications.TryNotifyOwnersAfterSaveAsync(
            existing,
            beforeSave,
            afterSave,
            cancellationToken);

        return saved;
    }

    private static int ResolveCalendarItemId(
        int postedCalendarItemId,
        string title,
        int sortOrder,
        IReadOnlyList<ProjectCalendarItem> afterSave,
        HashSet<int> usedIds)
    {
        if (postedCalendarItemId > 0)
        {
            var byId = afterSave.FirstOrDefault(a => a.CalendarItemID == postedCalendarItemId);
            if (byId is not null)
            {
                usedIds.Add(byId.CalendarItemID);
                return byId.CalendarItemID;
            }
        }

        var match = afterSave.FirstOrDefault(a =>
            !usedIds.Contains(a.CalendarItemID)
            && a.SortOrder == sortOrder
            && string.Equals((a.Title ?? string.Empty).Trim(), title, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            usedIds.Add(match.CalendarItemID);
            return match.CalendarItemID;
        }

        match = afterSave.FirstOrDefault(a =>
            !usedIds.Contains(a.CalendarItemID)
            && string.Equals((a.Title ?? string.Empty).Trim(), title, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            usedIds.Add(match.CalendarItemID);
            return match.CalendarItemID;
        }

        return 0;
    }

    public async Task<ProjectTimeWeekResponse> GetTimeWeekAsync(
        int projectId,
        DateTime weekStartDate,
        int? userId = null,
        CancellationToken cancellationToken = default)
    {
        if (projectId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(projectId));
        }

        var monday = StartOfWeekMonday(weekStartDate);
        var catUsers = await _projects.ListCatDepartmentUsersForProjectAsync(projectId, cancellationToken);
        var effectiveUserId = ResolveTimeTrackerUserId(userId, catUsers);

        var tasks = await _projects.ListCalendarItemsAsync(projectId, cancellationToken);
        var entries = await _projects.ListTimeEntriesForWeekAsync(
            projectId,
            monday,
            effectiveUserId,
            cancellationToken);

        return new ProjectTimeWeekResponse
        {
            WeekStartDate = monday,
            WeekEndDate = monday.AddDays(6),
            UserID = effectiveUserId,
            CatUsers = catUsers,
            Tasks = tasks,
            Entries = entries
        };
    }

    public async Task<ProjectTimeSummary> GetTimeSummaryAsync(int projectId, CancellationToken cancellationToken = default)
    {
        if (projectId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(projectId));
        }

        var existing = await _projects.GetAsync(projectId, cancellationToken);
        if (existing is null)
        {
            throw new InvalidOperationException($"Project {projectId} was not found.");
        }

        return await _projects.GetTimeSummaryAsync(projectId, cancellationToken);
    }

    public async Task<int> SaveTimeWeekAsync(
        int projectId,
        ProjectTimeWeekRequest request,
        CancellationToken cancellationToken = default)
    {
        if (projectId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(projectId));
        }

        ArgumentNullException.ThrowIfNull(request);

        var existing = await _projects.GetAsync(projectId, cancellationToken);
        if (existing is null)
        {
            throw new InvalidOperationException($"Project {projectId} was not found.");
        }

        var catUsers = await _projects.ListCatDepartmentUsersForProjectAsync(projectId, cancellationToken);
        var effectiveUserId = ResolveTimeTrackerUserId(request.UserID, catUsers);

        var monday = StartOfWeekMonday(request.WeekStartDate);
        request.Entries ??= [];
        await _projects.SaveTimeEntriesForWeekAsync(projectId, monday, request.Entries, effectiveUserId, cancellationToken);
        var saved = request.Entries.Count(e => e.CalendarItemID > 0 && e.Hours > 0m);
        _logger?.LogInformation(
            "Saved {Count} time entry cell(s) for ProjectID {ProjectID} user {UserID} week {Week}.",
            saved,
            projectId,
            effectiveUserId,
            monday);
        return saved;
    }

    private static int ResolveTimeTrackerUserId(int? requestedUserId, IReadOnlyList<ProjectTimeCatUser> catUsers)
    {
        if (requestedUserId is > 0)
        {
            if (catUsers.Count > 0 && !catUsers.Any(u => u.UserID == requestedUserId.Value))
            {
                throw new InvalidOperationException("The selected user is not a CAT-tagged member of this project's user group.");
            }

            return requestedUserId.Value;
        }

        if (catUsers.Count > 0)
        {
            return catUsers[0].UserID;
        }

        return CmsDefaults.ActingUserId;
    }

    public async Task<IReadOnlyList<ProjectFile>> ListFilesAsync(
        int projectId,
        CancellationToken cancellationToken = default)
    {
        if (projectId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(projectId));
        }

        var rows = await _projects.ListFilesAsync(projectId, cancellationToken);
        foreach (var row in rows)
        {
            row.DownloadUrl = _blobs.BuildDownloadUrl(row.BlobPath);
        }

        return rows;
    }

    public async Task<IReadOnlyList<ProjectFile>> UploadFilesAsync(
        int projectId,
        IReadOnlyList<(Stream Content, string FileName, string? ContentType, long Length)> files,
        string category = "SourceData",
        string? description = null,
        string? versionLabel = null,
        DateTime? dataAsOf = null,
        bool isCurrent = false,
        CancellationToken cancellationToken = default)
    {
        if (projectId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(projectId));
        }

        ArgumentNullException.ThrowIfNull(files);
        if (files.Count == 0)
        {
            throw new InvalidOperationException("Choose at least one file to upload.");
        }

        var existing = await _projects.GetAsync(projectId, cancellationToken);
        if (existing is null)
        {
            throw new InvalidOperationException($"Project {projectId} was not found.");
        }

        var uploaded = new List<ProjectFile>();
        var clientShortName = !string.IsNullOrWhiteSpace(existing.ClientShortName)
            ? existing.ClientShortName!
            : (!string.IsNullOrWhiteSpace(existing.ClientName)
                ? existing.ClientName!
                : $"Client{existing.ClientID}");
        var renewalYear = existing.RenewalDate?.Year ?? DateTime.UtcNow.Year;

        foreach (var file in files)
        {
            if (file.Length <= 0 || string.IsNullOrWhiteSpace(file.FileName))
            {
                continue;
            }

            if (file.Length > _blobs.MaxUploadBytes)
            {
                throw new InvalidOperationException(
                    $"'{file.FileName}' exceeds the maximum size ({_blobs.MaxUploadBytes / (1024 * 1024)} MB).");
            }

            var originalFileName = Path.GetFileName(file.FileName);
            // Reserve ProjectFileID first so the blob path can include it.
            var pendingPath = $"projects/_pending/{projectId}/{Guid.NewGuid():N}";
            var pendingUrl = pendingPath;
            var meta = new ProjectFile
            {
                ProjectID = projectId,
                OriginalFileName = originalFileName,
                ContentType = file.ContentType,
                ByteLength = file.Length,
                BlobPath = pendingPath,
                BlobUrl = pendingUrl,
                Category = category,
                Description = description,
                VersionLabel = versionLabel,
                DataAsOf = dataAsOf,
                IsCurrent = isCurrent,
                UploadedByUserID = CmsDefaults.ActingUserId
            };

            var newId = await _projects.AddFileAsync(meta, cancellationToken);
            meta.ProjectFileID = newId;

            string? blobPath = null;
            try
            {
                (blobPath, var blobUrl) = await _blobs.UploadAsync(
                    clientShortName,
                    renewalYear,
                    projectId,
                    newId,
                    originalFileName,
                    file.ContentType,
                    file.Content,
                    file.Length,
                    cancellationToken);

                await _projects.UpdateFileBlobLocationAsync(newId, blobPath, blobUrl, cancellationToken);
                meta.BlobPath = blobPath;
                meta.BlobUrl = blobUrl;
                meta.UploadedByName = null;
                meta.CreatedDate = DateTime.UtcNow;
                meta.DownloadUrl = _blobs.BuildDownloadUrl(blobPath);
                uploaded.Add(meta);
            }
            catch
            {
                if (!string.IsNullOrWhiteSpace(blobPath))
                {
                    try
                    {
                        await _blobs.DeleteAsync(blobPath, cancellationToken);
                    }
                    catch (Exception cleanupEx)
                    {
                        _logger?.LogWarning(
                            cleanupEx,
                            "Failed to clean up blob {BlobPath} after upload failure for ProjectFileID {FileId}.",
                            blobPath,
                            newId);
                    }
                }

                try
                {
                    await _projects.SoftDeleteFileAsync(newId, cancellationToken);
                }
                catch (Exception cleanupEx)
                {
                    _logger?.LogWarning(
                        cleanupEx,
                        "Failed to soft-delete ProjectFileID {FileId} after upload failure.",
                        newId);
                }

                throw;
            }
        }

        if (uploaded.Count == 0)
        {
            throw new InvalidOperationException("No valid files were uploaded.");
        }

        // Reload so uploader display name comes from SQL join.
        return await ListFilesAsync(projectId, cancellationToken);
    }

    public async Task<IReadOnlyList<ProjectFile>> UpdateFileAsync(
        int projectId,
        int projectFileId,
        ProjectFileUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        if (projectId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(projectId));
        }

        if (projectFileId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(projectFileId));
        }

        ArgumentNullException.ThrowIfNull(request);

        var file = await _projects.GetFileAsync(projectFileId, cancellationToken);
        if (file is null || file.ProjectID != projectId)
        {
            throw new InvalidOperationException("Project file was not found.");
        }

        // Null means leave unchanged; empty string clears the field.
        if (request.Category is not null
            || request.Description is not null
            || request.VersionLabel is not null
            || request.DataAsOf.HasValue
            || request.ClearDataAsOf)
        {
            var nextDataAsOf = request.ClearDataAsOf
                ? null
                : (request.DataAsOf.HasValue ? request.DataAsOf : file.DataAsOf);

            await _projects.UpdateFileMetadataAsync(
                projectFileId,
                request.Category,
                request.Description is not null ? request.Description : file.Description,
                request.VersionLabel is not null ? request.VersionLabel : file.VersionLabel,
                nextDataAsOf,
                cancellationToken);
        }

        if (request.IsCurrent.HasValue)
        {
            await _projects.SetFileCurrentAsync(projectFileId, request.IsCurrent.Value, cancellationToken);
        }

        return await ListFilesAsync(projectId, cancellationToken);
    }

    public async Task SoftDeleteFileAsync(
        int projectId,
        int projectFileId,
        CancellationToken cancellationToken = default)
    {
        if (projectId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(projectId));
        }

        if (projectFileId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(projectFileId));
        }

        var file = await _projects.GetFileAsync(projectFileId, cancellationToken);
        if (file is null || file.ProjectID != projectId)
        {
            throw new InvalidOperationException("Project file was not found.");
        }

        await _projects.SoftDeleteFileAsync(projectFileId, cancellationToken);

        try
        {
            await _blobs.DeleteAsync(file.BlobPath, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Soft-deleted ProjectFileID {FileId} but blob delete failed for {BlobPath}.", projectFileId, file.BlobPath);
        }
    }

    public async Task<(ProjectFile File, Stream Content)> OpenFileContentAsync(
        int projectId,
        int projectFileId,
        CancellationToken cancellationToken = default)
    {
        if (projectId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(projectId));
        }

        if (projectFileId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(projectFileId));
        }

        var file = await _projects.GetFileAsync(projectFileId, cancellationToken);
        if (file is null || file.ProjectID != projectId || !file.IsActive)
        {
            throw new InvalidOperationException("Project file was not found.");
        }

        if (string.IsNullOrWhiteSpace(file.BlobPath))
        {
            throw new InvalidOperationException("Project file has no stored content.");
        }

        var stream = await _blobs.OpenReadAsync(file.BlobPath, cancellationToken);
        file.DownloadUrl = _blobs.BuildDownloadUrl(file.BlobPath);
        return (file, stream);
    }

    private static DateTime StartOfWeekMonday(DateTime date)
    {
        var d = date.Date;
        var diff = ((int)d.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return d.AddDays(-diff);
    }

    public async Task<IReadOnlyList<object>> GetUserGroupMemberOptionsAsync(
        int ugid,
        CancellationToken cancellationToken = default)
    {
        if (ugid <= 0)
        {
            return [];
        }

        var members = await _admin.ListUserGroupMembersAsync(ugid, cancellationToken);
        return members
            .GroupBy(m => m.UserID)
            .Select(g =>
            {
                var m = g.First();
                var roles = string.Join(
                    ", ",
                    g.Select(x => (x.UserRoleName ?? string.Empty).Trim())
                        .Where(r => r.Length > 0)
                        .Distinct(StringComparer.OrdinalIgnoreCase));
                return new
                {
                    userID = m.UserID,
                    userFullName = string.IsNullOrWhiteSpace(m.UserFullName)
                        ? (m.Username ?? $"User {m.UserID}")
                        : m.UserFullName.Trim(),
                    title = (m.Title ?? string.Empty).Trim(),
                    email = (m.Email ?? string.Empty).Trim(),
                    phone = (m.Phone ?? string.Empty).Trim(),
                    userRoleName = roles,
                    userRoleID = m.UserRoleID
                };
            })
            .OrderBy(m => m.userFullName, StringComparer.OrdinalIgnoreCase)
            .Cast<object>()
            .ToList();
    }

    public async Task<IReadOnlyList<ProjectTimeCatUser>> ListCatDepartmentUsersForProjectAsync(
        int projectId,
        CancellationToken cancellationToken = default)
    {
        if (projectId <= 0)
        {
            return [];
        }

        return await _projects.ListCatDepartmentUsersForProjectAsync(projectId, cancellationToken);
    }

    public Task<IReadOnlyList<ActivityLog>> GetActivityAsync(int projectId, int top = 100, CancellationToken cancellationToken = default)
        => _admin.GetActivityForEntityAsync(ProjectEntityType, projectId, top, cancellationToken);

    private async Task<ProjectFormViewModel> PopulateListsAsync(ProjectFormViewModel model, CancellationToken cancellationToken)
    {
        var clients = await _clients.ListAsync(projectEligibleOnly: true, cancellationToken: cancellationToken);
        var businessUnits = await _referenceData.GetBusinessUnitsAsync(activeOnly: true, cancellationToken);
        var businessClasses = await _referenceData.GetBusinessClassesAsync(cancellationToken);
        var clientCategories = await _referenceData.GetClientCategoriesAsync(activeOnly: true, cancellationToken);
        var classificationLookups = await GetClientClassificationLookupsAsync(cancellationToken);
        var projectTypes = await _referenceData.GetProjectTypesAsync(cancellationToken);
        var statuses = await _referenceData.GetStatusesForEntityAsync(ProjectEntityType, cancellationToken);

        // Default to Active on a new project.
        if (model.StatusID == 0)
        {
            model.StatusID = statuses.FirstOrDefault(s => string.Equals(s.StatusCode, "A", StringComparison.OrdinalIgnoreCase))?.StatusID
                ?? statuses.FirstOrDefault()?.StatusID
                ?? 0;
        }

        model.ClientDirectoryRows = clients
            .OrderBy(c => c.ClientName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        model.ClientOptions = clients
            .Where(c => !string.IsNullOrWhiteSpace(c.ClientName))
            .Select(c => new SelectListItem(c.ClientName!.Trim(), c.ClientID.ToString(CultureInfo.InvariantCulture), c.ClientID == model.ClientID))
            .ToList();

        model.BusinessUnitOptions = businessUnits
            .Select(b => new SelectListItem(b.BusinessUnitName, b.BusinessUnitID.ToString(CultureInfo.InvariantCulture)))
            .ToList();

        model.BusinessClassOptions = businessClasses
            .Select(c =>
            {
                var label = BusinessClass.FormatDisplayDescription(c.BusinessClassName, c.BusinessClassDescription);
                return new SelectListItem(label ?? string.Empty, c.BusinessClassID.ToString(CultureInfo.InvariantCulture));
            })
            .ToList();

        model.ClientCategoryOptions = clientCategories
            .Select(c => new SelectListItem(c.DisplayName, c.ClientCategoryID.ToString(CultureInfo.InvariantCulture)))
            .ToList();

        model.ClientTypeOptions = classificationLookups.ClientTypeOptions
            .Select(o => new SelectListItem(
                o.Text,
                o.Value,
                model.ClientTypeID is > 0 && o.Value == model.ClientTypeID.Value.ToString(CultureInfo.InvariantCulture)))
            .ToList();

        model.ClientStatusOptions = classificationLookups.ClientStatusOptions
            .Select(o => new SelectListItem(
                o.Text,
                o.Value,
                model.ClientStatusID is > 0 && o.Value == model.ClientStatusID.Value.ToString(CultureInfo.InvariantCulture)))
            .ToList();

        model.ProjectTypeOptions = projectTypes
            .Select(pt => new SelectListItem(pt.DisplayName, pt.ProjectTypeID.ToString(CultureInfo.InvariantCulture), pt.ProjectTypeID == model.ProjectTypeID))
            .ToList();

        model.StatusOptions = statuses
            .Select(s => new SelectListItem(s.DisplayName, s.StatusID.ToString(CultureInfo.InvariantCulture), s.StatusID == model.StatusID))
            .ToList();

        // Full active user-group list (used-on-client groups sorted first when ClientID > 0).
        model.UserGroupOptions = model.ClientID > 0
            ? await GetUserGroupSelectListForClientAsync(model.ClientID, cancellationToken)
            : (await _referenceData.GetTopUserGroupSummariesAsync(null, cancellationToken))
                .Select(g => new SelectListItem(
                    string.IsNullOrWhiteSpace(g.UGName) ? $"Group {g.UserGroupNumber}" : $"{g.UGName} ({g.UserGroupNumber})",
                    g.UGID.ToString(CultureInfo.InvariantCulture),
                    g.UGID == model.UGID))
                .ToList();

        if (model.ProjectID is > 0 && model.ClientID > 0)
        {
            await RestrictClientListsToProjectClientAsync(model, cancellationToken);
        }

        return model;
    }

    private async Task RestrictClientListsToProjectClientAsync(ProjectFormViewModel model, CancellationToken cancellationToken)
    {
        var row = model.ClientDirectoryRows.FirstOrDefault(c => c.ClientID == model.ClientID);
        if (row is null)
        {
            var client = await _clients.GetAsync(model.ClientID, cancellationToken);
            if (client is not null)
            {
                row = ToClientListRow(client);
            }
        }

        if (row is null)
        {
            return;
        }

        if (model.ClientBusinessUnitID is > 0
            || model.ClientBusinessClassID is > 0
            || model.ClientTypeID is > 0
            || model.ClientStatusID is > 0)
        {
            row = new ClientListRow
            {
                ClientID = row.ClientID,
                ClientName = row.ClientName,
                ClientShortName = row.ClientShortName,
                BusinessUnitID = model.ClientBusinessUnitID is > 0 ? model.ClientBusinessUnitID : row.BusinessUnitID,
                BusinessUnitName = model.ClientBusinessUnitID is > 0
                    ? model.ClientBusinessUnitName ?? row.BusinessUnitName
                    : row.BusinessUnitName,
                BusinessClassID = model.ClientBusinessClassID is > 0 ? model.ClientBusinessClassID : row.BusinessClassID,
                BusinessClassName = model.ClientBusinessClassID is > 0
                    ? model.ClientBusinessClassName ?? row.BusinessClassName
                    : row.BusinessClassName,
                BusinessClassDescription = row.BusinessClassDescription,
                ClientCategoryID = row.ClientCategoryID,
                CCName = row.CCName,
                ClientTypeID = model.ClientTypeID is > 0 ? model.ClientTypeID : row.ClientTypeID,
                CTName = model.ClientTypeID is > 0 ? model.ClientTypeName ?? row.CTName : row.CTName,
                CTDescription = row.CTDescription,
                StatusID = model.ClientStatusID is > 0 ? model.ClientStatusID : row.StatusID,
                StatusCode = model.ClientStatusID is > 0 ? model.ClientStatusCode ?? row.StatusCode : row.StatusCode,
                StatusDescription = row.StatusDescription,
                CreatedDate = row.CreatedDate,
                ModifiedDate = row.ModifiedDate,
            };
        }

        model.ClientDirectoryRows = [row];
        model.ClientOptions =
        [
            new SelectListItem(
                row.ClientName?.Trim() ?? $"Client {row.ClientID}",
                row.ClientID.ToString(CultureInfo.InvariantCulture),
                true)
        ];

        model.ClientName = row.ClientName ?? model.ClientName;
        model.ClientShortName = row.ClientShortName ?? model.ClientShortName;
        model.ClientBusinessUnitID = model.ClientBusinessUnitID is > 0
            ? model.ClientBusinessUnitID
            : row.BusinessUnitID ?? model.ClientBusinessUnitID;
        model.ClientBusinessUnitName = model.ClientBusinessUnitID is > 0
            ? model.ClientBusinessUnitName ?? row.BusinessUnitName
            : row.BusinessUnitName ?? model.ClientBusinessUnitName;
        model.ClientBusinessClassID = model.ClientBusinessClassID is > 0
            ? model.ClientBusinessClassID
            : row.BusinessClassID ?? model.ClientBusinessClassID;
        model.ClientBusinessClassName = model.ClientBusinessClassID is > 0
            ? model.ClientBusinessClassName
            : BusinessClass.FormatDisplayDescription(row.BusinessClassName, row.BusinessClassDescription);
        model.ClientCategoryID = row.ClientCategoryID ?? model.ClientCategoryID;
        model.ClientCategoryName = row.CCName ?? model.ClientCategoryName;
        model.ClientTypeID = model.ClientTypeID is > 0
            ? model.ClientTypeID
            : row.ClientTypeID ?? model.ClientTypeID;
        model.ClientTypeName = model.ClientTypeID is > 0
            ? model.ClientTypeName
            : ClientType.FormatDisplayName(row.CTName, row.CTDescription);
        model.ClientStatusID = model.ClientStatusID is > 0
            ? model.ClientStatusID
            : row.StatusID ?? model.ClientStatusID;
        model.ClientStatusCode = model.ClientStatusID is > 0
            ? model.ClientStatusCode
            : row.StatusCode ?? row.StatusDescription ?? model.ClientStatusCode;
    }

    private static ClientListRow ToClientListRow(Client client) => new()
    {
        ClientID = client.ClientID,
        ClientName = client.ClientName,
        ClientShortName = client.ClientShortName,
        BusinessUnitID = client.BusinessUnitID,
        BusinessUnitName = client.BusinessUnitName,
        BusinessClassID = client.BusinessClassID > 0 ? client.BusinessClassID : null,
        BusinessClassName = client.BusinessClassName,
        BusinessClassDescription = client.BusinessClassDescription,
        ClientCategoryID = client.ClientCategoryID > 0 ? client.ClientCategoryID : null,
        CCName = client.CCName,
        ClientTypeID = client.ClientTypeID > 0 ? client.ClientTypeID : null,
        CTName = client.CTName,
        CTDescription = client.CTDescription,
        StatusID = client.StatusID > 0 ? client.StatusID : null,
        StatusCode = client.StatusCode,
        StatusDescription = client.StatusDescription,
        CreatedDate = client.CreatedDate,
        ModifiedDate = client.ModifiedDate
    };
}
