namespace ClientManagementSystem.Services;

/// <summary>Generates the CMS project report PDF, saves it to project blob storage, and returns a download URL.</summary>
public interface IProjectReportService
{
    /// <summary>
    /// Builds the same project report used by the Project page, uploads it as CorrespondenceData,
    /// and returns the SAS download URL for the saved file.
    /// </summary>
    Task<ProjectReportGenerationResult> GenerateAndSaveAsync(
        int projectId,
        string? fileName = null,
        bool overwriteSameName = true,
        CancellationToken cancellationToken = default);
}

public sealed class ProjectReportGenerationResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public int ProjectId { get; init; }
    public string? ProjectNumber { get; init; }
    public string? ProjectName { get; init; }
    public int? ProjectFileId { get; init; }
    public string? FileName { get; init; }
    /// <summary>Friendly chat link text, e.g. ClientName_ProjectName_ProjectReport.pdf.</summary>
    public string? LinkLabel { get; init; }
    public string? DownloadUrl { get; init; }
    public string? Category { get; init; }
    public string? Message { get; init; }
}
