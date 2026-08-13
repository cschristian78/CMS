namespace ClientManagementSystem.Models;

/// <summary>Row in <c>dbo.tProjectFile</c> — client-data file metadata (bytes live in Azure Blob).</summary>
public sealed class ProjectFile
{
    public int ProjectFileID { get; set; }
    public int ProjectID { get; set; }
    public int ProjectRefID { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public string? ContentType { get; set; }
    public long ByteLength { get; set; }
    public string BlobPath { get; set; } = string.Empty;
    public string BlobUrl { get; set; } = string.Empty;

    /// <summary>SourceData | ExposureData | LossData | ClaimsData</summary>
    public string Category { get; set; } = "SourceData";

    public string? Description { get; set; }

    /// <summary>Version control label (e.g. v1, 2026-08-12).</summary>
    public string? VersionLabel { get; set; }

    /// <summary>Data-as-of date for the uploaded dataset.</summary>
    public DateTime? DataAsOf { get; set; }

    /// <summary>Process-Active flag: the file currently marked for processing (at most one per project).</summary>
    public bool IsCurrent { get; set; }

    public int UploadedByUserID { get; set; }
    public string? UploadedByName { get; set; }

    /// <summary>Soft-delete flag (row still exists when false).</summary>
    public bool IsActive { get; set; } = true;

    public DateTime? CreatedDate { get; set; }
    public DateTime? ModifiedDate { get; set; }

    /// <summary>Server-built download URL (blob path + SAS). Not persisted.</summary>
    public string? DownloadUrl { get; set; }
}

/// <summary>Body for updating project-file metadata (description / version / data-as-of / process-Active).</summary>
public sealed class ProjectFileUpdateRequest
{
    public string? Description { get; set; }
    public string? VersionLabel { get; set; }
    public DateTime? DataAsOf { get; set; }
    /// <summary>When true, clears DataAsOf even if <see cref="DataAsOf"/> is null.</summary>
    public bool ClearDataAsOf { get; set; }
    public bool? IsCurrent { get; set; }
}

public sealed class AzureBlobOptions
{
    public const string SectionName = "AzureBlob";

    /// <summary>Container base URL without SAS, e.g. https://account.blob.core.windows.net/cms</summary>
    public string ContainerBaseUrl { get; set; } = string.Empty;

    /// <summary>SAS query string (with or without leading ?).</summary>
    public string SasToken { get; set; } = string.Empty;

    public long MaxUploadBytes { get; set; } = 50 * 1024 * 1024;
}
