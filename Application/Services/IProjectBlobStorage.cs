namespace ClientManagementSystem.Services;

public interface IProjectBlobStorage
{
    long MaxUploadBytes { get; }

    /// <summary>
    /// Uploads a stream to
    /// <c>projects/{clientShortName}/{renewalYear}/{projectId}/{projectFileId}/{fileName}</c>
    /// and returns (blobPath, blobUrlWithoutSas).
    /// </summary>
    Task<(string BlobPath, string BlobUrl)> UploadAsync(
        string clientShortName,
        int renewalYear,
        int projectId,
        int projectFileId,
        string originalFileName,
        string? contentType,
        Stream content,
        long contentLength,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(string blobPath, CancellationToken cancellationToken = default);

    /// <summary>Builds a SAS-authenticated download URL for a stored blob path or blob URL.</summary>
    string BuildDownloadUrl(string blobPathOrUrl);
}
