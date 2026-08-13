using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using ClientManagementSystem.Models;
using Microsoft.Extensions.Options;

namespace ClientManagementSystem.Services;

public sealed class AzureProjectBlobStorage : IProjectBlobStorage
{
    private readonly AzureBlobOptions _options;
    private readonly BlobContainerClient _container;

    public AzureProjectBlobStorage(IOptions<AzureBlobOptions> options)
    {
        _options = options.Value ?? new AzureBlobOptions();
        var baseUrl = (_options.ContainerBaseUrl ?? string.Empty).Trim().TrimEnd('/');
        var sas = (_options.SasToken ?? string.Empty).Trim().TrimStart('?');
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(sas))
        {
            throw new InvalidOperationException(
                "AzureBlob:ContainerBaseUrl and AzureBlob:SasToken must be configured.");
        }

        _container = new BlobContainerClient(new Uri($"{baseUrl}?{sas}"));
    }

    public long MaxUploadBytes => _options.MaxUploadBytes > 0 ? _options.MaxUploadBytes : 50 * 1024 * 1024;

    public async Task<(string BlobPath, string BlobUrl)> UploadAsync(
        int projectId,
        string originalFileName,
        string? contentType,
        Stream content,
        long contentLength,
        CancellationToken cancellationToken = default)
    {
        if (projectId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(projectId));
        }

        if (contentLength > MaxUploadBytes)
        {
            throw new InvalidOperationException($"File exceeds maximum size ({MaxUploadBytes / (1024 * 1024)} MB).");
        }

        var safeName = SanitizeFileName(originalFileName);
        var now = DateTime.UtcNow;
        var blobPath = $"projects/{projectId}/{now:yyyy}/{now:MM}/{Guid.NewGuid():N}_{safeName}";
        var blob = _container.GetBlobClient(blobPath);

        var headers = new BlobHttpHeaders
        {
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType
        };

        await blob.UploadAsync(
            content,
            new BlobUploadOptions { HttpHeaders = headers },
            cancellationToken);

        var blobUrl = $"{_options.ContainerBaseUrl.Trim().TrimEnd('/')}/{blobPath}";
        return (blobPath, blobUrl);
    }

    public async Task DeleteAsync(string blobPath, CancellationToken cancellationToken = default)
    {
        var path = NormalizeBlobPath(blobPath);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var blob = _container.GetBlobClient(path);
        await blob.DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: cancellationToken);
    }

    public string BuildDownloadUrl(string blobPathOrUrl)
    {
        var path = NormalizeBlobPath(blobPathOrUrl);
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var baseUrl = _options.ContainerBaseUrl.Trim().TrimEnd('/');
        var sas = _options.SasToken.Trim().TrimStart('?');
        return $"{baseUrl}/{path}?{sas}";
    }

    private string NormalizeBlobPath(string blobPathOrUrl)
    {
        var value = (blobPathOrUrl ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var baseUrl = _options.ContainerBaseUrl.Trim().TrimEnd('/');
        if (value.StartsWith(baseUrl, StringComparison.OrdinalIgnoreCase))
        {
            value = value[baseUrl.Length..].TrimStart('/');
        }

        var q = value.IndexOf('?', StringComparison.Ordinal);
        if (q >= 0)
        {
            value = value[..q];
        }

        return value.TrimStart('/');
    }

    private static string SanitizeFileName(string? originalFileName)
    {
        var name = Path.GetFileName(originalFileName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            name = "upload.bin";
        }

        foreach (var c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }

        name = name.Replace(' ', '_');
        if (name.Length > 180)
        {
            var ext = Path.GetExtension(name);
            var stem = Path.GetFileNameWithoutExtension(name);
            stem = stem[..Math.Min(stem.Length, 180 - ext.Length)];
            name = stem + ext;
        }

        return name;
    }
}
