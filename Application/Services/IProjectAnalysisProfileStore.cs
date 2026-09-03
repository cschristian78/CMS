namespace ClientManagementSystem.Services;

public interface IProjectAnalysisProfileStore
{
    Task<string?> LoadTemplateJsonAsync(CancellationToken cancellationToken = default);
    Task<string?> LoadProjectJsonAsync(int projectId, CancellationToken cancellationToken = default);
    Task SaveProjectJsonAsync(int projectId, string json, CancellationToken cancellationToken = default);
}
