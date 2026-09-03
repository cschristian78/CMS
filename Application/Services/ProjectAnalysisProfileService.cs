using System.Text.Json;
using ClientManagementSystem.Data;

namespace ClientManagementSystem.Services;

public interface IProjectAnalysisProfileService
{
    Task<(string Json, bool FromTemplate)> GetProfileJsonAsync(int projectId, CancellationToken cancellationToken = default);
    Task SaveProfileJsonAsync(int projectId, string json, CancellationToken cancellationToken = default);
}

public sealed class ProjectAnalysisProfileService : IProjectAnalysisProfileService
{
    private readonly IProjectAnalysisProfileStore _store;
    private readonly IProjectRepository _projects;

    public ProjectAnalysisProfileService(IProjectAnalysisProfileStore store, IProjectRepository projects)
    {
        _store = store;
        _projects = projects;
    }

    public async Task<(string Json, bool FromTemplate)> GetProfileJsonAsync(int projectId, CancellationToken cancellationToken = default)
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

        var saved = await _store.LoadProjectJsonAsync(projectId, cancellationToken);
        if (!string.IsNullOrWhiteSpace(saved))
        {
            return (saved, false);
        }

        var template = await _store.LoadTemplateJsonAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(template))
        {
            return (template, true);
        }

        return ("{}", true);
    }

    public async Task SaveProfileJsonAsync(int projectId, string json, CancellationToken cancellationToken = default)
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

        try
        {
            using var doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Analysis profile must be valid JSON.", ex);
        }

        await _store.SaveProjectJsonAsync(projectId, json, cancellationToken);
    }
}
