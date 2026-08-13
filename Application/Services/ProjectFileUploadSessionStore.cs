using System.Collections.Concurrent;
using System.Text.Json;
using ClientManagementSystem.Models;

namespace ClientManagementSystem.Services;

public sealed class ProjectFileUploadSessionInfo
{
    public string SessionId { get; init; } = string.Empty;
    public int ProjectId { get; init; }
    public string OriginalFileName { get; init; } = string.Empty;
    public string? ContentType { get; init; }
    public long ByteLength { get; init; }
    public string Category { get; init; } = "SourceData";
    public string? Description { get; init; }
    public string? VersionLabel { get; init; }
    public DateTime? DataAsOf { get; init; }
    public string DirectoryPath { get; init; } = string.Empty;
    public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;
}

public interface IProjectFileUploadSessionStore
{
    /// <summary>Preferred chunk size so each IIS request stays under the default ~28 MB filter.</summary>
    int ChunkSizeBytes { get; }

    ProjectFileUploadSessionInfo Create(
        int projectId,
        string originalFileName,
        string? contentType,
        long byteLength,
        string category,
        string? description,
        string? versionLabel,
        DateTime? dataAsOf);

    ProjectFileUploadSessionInfo? Get(string sessionId);

    Task SaveChunkAsync(string sessionId, int chunkIndex, Stream content, CancellationToken cancellationToken = default);

    Task<Stream> OpenAssembledStreamAsync(string sessionId, CancellationToken cancellationToken = default);

    void Delete(string sessionId);
}

public sealed class ProjectFileUploadSessionStore : IProjectFileUploadSessionStore
{
    public const int DefaultChunkSizeBytes = 4 * 1024 * 1024; // 4 MB — safely under IIS default ~28.6 MB

    private readonly ConcurrentDictionary<string, ProjectFileUploadSessionInfo> _sessions = new(StringComparer.Ordinal);
    private readonly string _root;
    private readonly ILogger<ProjectFileUploadSessionStore> _logger;

    public ProjectFileUploadSessionStore(IWebHostEnvironment env, ILogger<ProjectFileUploadSessionStore> logger)
    {
        _root = Path.Combine(env.ContentRootPath, "App_Data", "UploadSessions");
        Directory.CreateDirectory(_root);
        _logger = logger;
    }

    public int ChunkSizeBytes => DefaultChunkSizeBytes;

    public ProjectFileUploadSessionInfo Create(
        int projectId,
        string originalFileName,
        string? contentType,
        long byteLength,
        string category,
        string? description,
        string? versionLabel,
        DateTime? dataAsOf)
    {
        CleanupExpired();

        var sessionId = Guid.NewGuid().ToString("N");
        var dir = Path.Combine(_root, sessionId);
        Directory.CreateDirectory(dir);

        var meta = new ProjectFileUploadSessionInfo
        {
            SessionId = sessionId,
            ProjectId = projectId,
            OriginalFileName = Path.GetFileName(originalFileName),
            ContentType = contentType,
            ByteLength = byteLength,
            Category = string.IsNullOrWhiteSpace(category) ? "SourceData" : category.Trim(),
            Description = description,
            VersionLabel = versionLabel,
            DataAsOf = dataAsOf,
            DirectoryPath = dir,
            CreatedUtc = DateTime.UtcNow
        };

        File.WriteAllText(Path.Combine(dir, "meta.json"), JsonSerializer.Serialize(meta));
        _sessions[sessionId] = meta;
        return meta;
    }

    public ProjectFileUploadSessionInfo? Get(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return null;
        }

        if (_sessions.TryGetValue(sessionId, out var cached))
        {
            return cached;
        }

        var dir = Path.Combine(_root, sessionId);
        var jsonPath = Path.Combine(dir, "meta.json");
        if (!File.Exists(jsonPath))
        {
            return null;
        }

        try
        {
            var meta = JsonSerializer.Deserialize<ProjectFileUploadSessionInfo>(File.ReadAllText(jsonPath));
            if (meta is null)
            {
                return null;
            }

            _sessions[sessionId] = meta;
            return meta;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read upload session {SessionId}.", sessionId);
            return null;
        }
    }

    public async Task SaveChunkAsync(string sessionId, int chunkIndex, Stream content, CancellationToken cancellationToken = default)
    {
        var session = Get(sessionId) ?? throw new InvalidOperationException("Upload session was not found or expired.");
        if (chunkIndex < 0)
        {
            throw new InvalidOperationException("chunkIndex must be >= 0.");
        }

        var chunkPath = Path.Combine(session.DirectoryPath, $"chunk_{chunkIndex:D6}.part");
        await using var fs = new FileStream(chunkPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await content.CopyToAsync(fs, cancellationToken);
    }

    public Task<Stream> OpenAssembledStreamAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        var session = Get(sessionId) ?? throw new InvalidOperationException("Upload session was not found or expired.");
        var parts = Directory.GetFiles(session.DirectoryPath, "chunk_*.part")
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        if (parts.Count == 0)
        {
            throw new InvalidOperationException("No upload chunks were received.");
        }

        var assembledPath = Path.Combine(session.DirectoryPath, "assembled.bin");
        using (var output = new FileStream(assembledPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            foreach (var part in parts)
            {
                using var input = new FileStream(part, FileMode.Open, FileAccess.Read, FileShare.Read);
                input.CopyTo(output);
            }
        }

        var length = new FileInfo(assembledPath).Length;
        if (session.ByteLength > 0 && length != session.ByteLength)
        {
            throw new InvalidOperationException(
                $"Assembled size {length} does not match declared size {session.ByteLength}.");
        }

        Stream stream = new FileStream(assembledPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Task.FromResult(stream);
    }

    public void Delete(string sessionId)
    {
        _sessions.TryRemove(sessionId, out _);
        var dir = Path.Combine(_root, sessionId);
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete upload session folder {SessionId}.", sessionId);
        }
    }

    private void CleanupExpired()
    {
        var cutoff = DateTime.UtcNow.AddHours(-6);
        foreach (var dir in Directory.EnumerateDirectories(_root))
        {
            try
            {
                var created = Directory.GetCreationTimeUtc(dir);
                if (created < cutoff)
                {
                    var id = Path.GetFileName(dir);
                    _sessions.TryRemove(id, out _);
                    Directory.Delete(dir, recursive: true);
                }
            }
            catch
            {
                // best-effort cleanup
            }
        }
    }
}

public sealed class ProjectFileUploadSessionStartRequest
{
    public string FileName { get; set; } = string.Empty;
    public string? ContentType { get; set; }
    public long ByteLength { get; set; }
    public string? Category { get; set; }
    public string? Description { get; set; }
    public string? VersionLabel { get; set; }
    public DateTime? DataAsOf { get; set; }
}

public sealed class ProjectFileUploadSessionStartResponse
{
    public string SessionId { get; set; } = string.Empty;
    public int ChunkSizeBytes { get; set; }
    public long MaxUploadBytes { get; set; }
}
