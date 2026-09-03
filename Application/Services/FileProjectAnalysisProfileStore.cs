using Microsoft.AspNetCore.Hosting;

namespace ClientManagementSystem.Services;

public sealed class FileProjectAnalysisProfileStore : IProjectAnalysisProfileStore
{
    private readonly string _profilesDir;
    private readonly string _templatePath;

    public FileProjectAnalysisProfileStore(IWebHostEnvironment env)
    {
        var root = env.ContentRootPath;
        _profilesDir = Path.Combine(root, "App_Data", "AnalysisProfiles");
        _templatePath = Path.Combine(root, "Templates", "GSIC_BAR_Config.template.json");
        Directory.CreateDirectory(_profilesDir);
    }

    public Task<string?> LoadTemplateJsonAsync(CancellationToken cancellationToken = default)
        => ReadFileIfExistsAsync(_templatePath, cancellationToken);

    public Task<string?> LoadProjectJsonAsync(int projectId, CancellationToken cancellationToken = default)
        => ReadFileIfExistsAsync(ProjectPath(projectId), cancellationToken);

    public async Task SaveProjectJsonAsync(int projectId, string json, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        await File.WriteAllTextAsync(ProjectPath(projectId), json, cancellationToken);
    }

    private string ProjectPath(int projectId) => Path.Combine(_profilesDir, $"project-{projectId}.json");

    private static async Task<string?> ReadFileIfExistsAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        return await File.ReadAllTextAsync(path, cancellationToken);
    }
}
