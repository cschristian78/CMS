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
    private readonly ILogger<ProjectService>? _logger;

    public ProjectService(
        IProjectRepository projects,
        IClientRepository clients,
        IReferenceDataRepository referenceData,
        IAdminRepository admin,
        IDashboardRepository dashboard,
        ILogger<ProjectService>? logger = null)
    {
        _projects = projects;
        _clients = clients;
        _referenceData = referenceData;
        _admin = admin;
        _dashboard = dashboard;
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
            ClientBusinessUnitName = project.BusinessUnitName,
            ClientBusinessClassName = project.BusinessClassName
        };

        var client = await _clients.GetAsync(project.ClientID, cancellationToken);
        if (client is not null)
        {
            model.ClientName = client.ClientName;
            model.ClientShortName = client.ClientShortName;
            model.ClientBusinessUnitID = client.BusinessUnitID;
            model.ClientBusinessUnitName = client.BusinessUnitName;
            model.ClientBusinessClassID = client.BusinessClassID;
            model.ClientBusinessClassName = BusinessClass.FormatDisplayDescription(
                client.BusinessClassName,
                client.BusinessClassDescription);
            model.ClientCategoryID = client.ClientCategoryID;
            model.ClientCategoryName = client.CCName;
            model.ClientTypeID = client.ClientTypeID;
            model.ClientTypeName = ClientType.FormatDisplayName(client.CTName, client.CTDescription);
            model.ClientStatusID = client.StatusID;
            model.ClientStatusCode = client.StatusCode;
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
            UGID = model.UGID,
            ProjectTypeID = model.ProjectTypeID,
            RenewalDate = model.RenewalDate,
            DueDate = model.DueDate,
            StatusID = model.StatusID > 0 ? model.StatusID : 1
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

        var projectId = await _projects.SaveProjectAndReplaceServicesAsync(
            project,
            model.SelectedServiceIds,
            revenueEntry,
            cancellationToken);

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
        var used = await _referenceData.GetUserGroupsUsedOnClientProjectsAsync(clientId, cancellationToken);
        var source = used.Count > 0
            ? used
            : await _referenceData.GetTopUserGroupSummariesAsync(null, cancellationToken);

        return source
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

    public Task<int> AddNoteAsync(ProjectNote note, CancellationToken cancellationToken = default)
        => _projects.AddNoteAsync(note, cancellationToken);

    public Task<int> UpdateNoteAsync(ProjectNote note, CancellationToken cancellationToken = default)
        => _projects.UpdateNoteAsync(note, cancellationToken);

    public Task<int> DeleteNoteAsync(int noteId, CancellationToken cancellationToken = default)
        => _projects.DeleteNoteAsync(noteId, cancellationToken);

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

        // User group list narrows to those used by the selected client; falls back to top groups
        // overall when the client has no projects yet.
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
        model.ClientBusinessUnitID = row.BusinessUnitID ?? model.ClientBusinessUnitID;
        model.ClientBusinessUnitName = row.BusinessUnitName ?? model.ClientBusinessUnitName;
        model.ClientBusinessClassID = row.BusinessClassID ?? model.ClientBusinessClassID;
        model.ClientBusinessClassName = BusinessClass.FormatDisplayDescription(
            row.BusinessClassName,
            row.BusinessClassDescription);
        model.ClientCategoryID = row.ClientCategoryID ?? model.ClientCategoryID;
        model.ClientCategoryName = row.CCName ?? model.ClientCategoryName;
        model.ClientTypeID = row.ClientTypeID ?? model.ClientTypeID;
        model.ClientTypeName = ClientType.FormatDisplayName(row.CTName, row.CTDescription);
        model.ClientStatusID = row.StatusID ?? model.ClientStatusID;
        model.ClientStatusCode = row.StatusCode ?? row.StatusDescription ?? model.ClientStatusCode;
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
