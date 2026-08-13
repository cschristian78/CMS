using ClientManagementSystem.Models;
using ClientManagementSystem.Services;
using ClientManagementSystem.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace ClientManagementSystem.Api;

[Route("api/projects")]
[ApiController]
public sealed class ProjectsApiController : ControllerBase
{
    private readonly IProjectService _projectService;
    private readonly IProjectBlobStorage _blobs;
    private readonly IProjectFileUploadSessionStore _uploadSessions;
    private readonly ILogger<ProjectsApiController> _logger;

    public ProjectsApiController(
        IProjectService projectService,
        IProjectBlobStorage blobs,
        IProjectFileUploadSessionStore uploadSessions,
        ILogger<ProjectsApiController> logger)
    {
        _projectService = projectService;
        _blobs = blobs;
        _uploadSessions = uploadSessions;
        _logger = logger;
    }

    [HttpGet("form")]
    public async Task<ActionResult<ProjectFormViewModel>> GetCreateFormAsync(CancellationToken cancellationToken)
    {
        var model = await _projectService.PrepareCreateFormAsync(cancellationToken: cancellationToken);
        return Ok(model);
    }

    [HttpGet("form/{id:int}")]
    public async Task<ActionResult<ProjectFormViewModel>> GetEditFormAsync(int id, CancellationToken cancellationToken)
    {
        var model = await _projectService.PrepareEditFormAsync(id, cancellationToken);
        return model is null ? NotFound() : Ok(model);
    }

    /// <summary>Client Info dropdown options from <c>dbo.tClientType</c> and <c>dbo.tStatus</c>.</summary>
    [HttpGet("lookups/client-classification")]
    public async Task<ActionResult<ProjectClientClassificationLookups>> GetClientClassificationLookupsAsync(
        CancellationToken cancellationToken)
    {
        return Ok(await _projectService.GetClientClassificationLookupsAsync(cancellationToken));
    }

    [HttpGet("user-groups")]
    public async Task<ActionResult<IReadOnlyList<object>>> GetUserGroupsForClientAsync(
        [FromQuery] int clientId,
        CancellationToken cancellationToken)
    {
        if (clientId <= 0)
        {
            return BadRequest(new ProblemDetails { Title = "Invalid client", Detail = "clientId must be positive." });
        }

        var items = await _projectService.GetUserGroupSelectListForClientAsync(clientId, cancellationToken);
        return Ok(items.Select(i => new { value = i.Value, text = i.Text }).ToList());
    }

    [HttpGet("services-catalog")]
    public async Task<ActionResult<IReadOnlyList<ProjectCatalogServiceRow>>> GetServicesCatalogAsync(
        CancellationToken cancellationToken)
    {
        var rows = await _projectService.GetActiveServicesCatalogAsync(cancellationToken);
        return Ok(rows);
    }

    [HttpPost]
    public async Task<ActionResult<object>> SaveAsync(
        [FromBody] ProjectFormViewModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var projectId = await _projectService.SaveAsync(model, cancellationToken);
        return Ok(new { projectId, message = model.ProjectID.HasValue ? "Project updated successfully." : "Project saved successfully." });
    }

    [HttpPost("{id:int}/soft-delete")]
    public async Task<ActionResult<object>> SoftDeleteAsync(int id, CancellationToken cancellationToken)
    {
        await _projectService.SoftDeleteAsync(id, cancellationToken);
        return Ok(new { message = "Project soft-deleted." });
    }

    [HttpPost("{id:int}/restore")]
    public async Task<ActionResult<object>> RestoreAsync(int id, CancellationToken cancellationToken)
    {
        await _projectService.RestoreAsync(id, cancellationToken);
        return Ok(new { message = "Project restored." });
    }

    public sealed class SetOnlineRequest
    {
        public bool IsOnline { get; set; }
    }

    [HttpPost("{id:int}/online")]
    public async Task<ActionResult<object>> SetOnlineAsync(
        int id,
        [FromBody] SetOnlineRequest body,
        CancellationToken cancellationToken)
    {
        await _projectService.SetOnlineAsync(id, body.IsOnline, cancellationToken);
        return Ok(new
        {
            projectId = id,
            isOnline = body.IsOnline,
            message = body.IsOnline ? "Project is now Online." : "Project is now Offline."
        });
    }

    // ---- Sub-resources --------------------------------------------------

    [HttpGet("{id:int}/revenue")]
    public async Task<ActionResult<IReadOnlyList<ProjectRevenue>>> GetRevenueHistoryAsync(int id, CancellationToken cancellationToken)
    {
        var rows = await _projectService.GetRevenueHistoryAsync(id, cancellationToken);
        return Ok(rows);
    }

    [HttpPost("{id:int}/revenue")]
    public async Task<ActionResult<object>> AddRevenueAsync(int id, [FromBody] ProjectRevenue body, CancellationToken cancellationToken)
    {
        body.ProjectID = id;
        var revenueId = await _projectService.AddRevenueAsync(body, cancellationToken);
        return Ok(new { revenueId });
    }

    [HttpDelete("revenue/{revenueId:int}")]
    public async Task<IActionResult> DeleteRevenueAsync(int revenueId, CancellationToken cancellationToken)
    {
        await _projectService.DeleteRevenueAsync(revenueId, cancellationToken);
        return NoContent();
    }

    [HttpGet("{id:int}/notes")]
    public async Task<ActionResult<IReadOnlyList<ProjectNote>>> ListNotesAsync(int id, CancellationToken cancellationToken)
        => Ok(await _projectService.ListNotesAsync(id, cancellationToken));

    [HttpPost("{id:int}/notes")]
    public async Task<ActionResult<object>> AddNoteAsync(int id, [FromBody] ProjectNote body, CancellationToken cancellationToken)
    {
        body.ProjectID = id;
        var noteId = await _projectService.AddNoteAsync(body, cancellationToken);
        return Ok(new { noteId });
    }

    [HttpPut("notes/{noteId:int}")]
    public async Task<IActionResult> UpdateNoteAsync(int noteId, [FromBody] ProjectNote body, CancellationToken cancellationToken)
    {
        body.NoteID = noteId;
        await _projectService.UpdateNoteAsync(body, cancellationToken);
        return NoContent();
    }

    [HttpDelete("notes/{noteId:int}")]
    public async Task<IActionResult> DeleteNoteAsync(int noteId, CancellationToken cancellationToken)
    {
        await _projectService.DeleteNoteAsync(noteId, cancellationToken);
        return NoContent();
    }

    [HttpGet("{id:int}/activity")]
    public async Task<ActionResult<IReadOnlyList<ActivityLog>>> GetActivityAsync(
        int id,
        [FromQuery] int top = 100,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return Ok(await _projectService.GetActivityAsync(id, top, cancellationToken));
        }
        catch (Exception ex)
        {
            // Activity is supplementary on Edit Project — never fail the page for log read issues.
            _logger.LogWarning(ex, "Activity log load failed for ProjectID {ProjectID}.", id);
            return Ok(Array.Empty<ActivityLog>());
        }
    }

    [HttpGet("{id:int}/calendar")]
    public async Task<ActionResult<IReadOnlyList<ProjectCalendarItem>>> ListCalendarItemsAsync(
        int id,
        CancellationToken cancellationToken)
        => Ok(await _projectService.ListCalendarItemsAsync(id, cancellationToken));

    /// <summary>
    /// Replace Analytics Calendar rows for a project (create / update / soft-delete).
    /// Used by Edit Project Save so calendar persistence does not depend on nested form binding alone.
    /// </summary>
    [HttpPut("{id:int}/calendar")]
    [HttpPost("{id:int}/calendar")]
    public async Task<ActionResult<object>> SaveCalendarItemsAsync(
        int id,
        [FromBody] ProjectCalendarSaveRequest request,
        CancellationToken cancellationToken)
    {
        if (id <= 0)
        {
            return BadRequest(new ProblemDetails { Title = "Invalid project", Detail = "id must be positive." });
        }

        var items = request.Items ?? request.CalendarItems ?? [];
        try
        {
            var saved = await _projectService.SaveCalendarItemsAsync(id, items, cancellationToken);
            return Ok(new
            {
                projectId = id,
                savedCount = saved,
                message = $"Saved {saved} calendar item(s)."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Calendar save failed for ProjectID {ProjectID}.", id);
            return Problem(
                detail: ex.Message,
                title: "Calendar save failed",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    [HttpGet("{id:int}/time")]
    public async Task<ActionResult<ProjectTimeWeekResponse>> GetTimeWeekAsync(
        int id,
        [FromQuery] DateTime? weekStart,
        CancellationToken cancellationToken)
    {
        if (id <= 0)
        {
            return BadRequest(new ProblemDetails { Title = "Invalid project", Detail = "id must be positive." });
        }

        try
        {
            return Ok(await _projectService.GetTimeWeekAsync(id, weekStart ?? DateTime.Today, cancellationToken));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Time week load failed for ProjectID {ProjectID}.", id);
            return Problem(
                detail: ex.Message,
                title: "Time tracker load failed",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    [HttpPut("{id:int}/time")]
    [HttpPost("{id:int}/time")]
    public async Task<ActionResult<object>> SaveTimeWeekAsync(
        int id,
        [FromBody] ProjectTimeWeekRequest request,
        CancellationToken cancellationToken)
    {
        if (id <= 0)
        {
            return BadRequest(new ProblemDetails { Title = "Invalid project", Detail = "id must be positive." });
        }

        try
        {
            var saved = await _projectService.SaveTimeWeekAsync(id, request, cancellationToken);
            return Ok(new
            {
                projectId = id,
                savedCount = saved,
                message = $"Saved {saved} time entry cell(s)."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Time week save failed for ProjectID {ProjectID}.", id);
            return Problem(
                detail: ex.Message,
                title: "Time tracker save failed",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    [HttpGet("user-groups/{ugid:int}/members")]
    public async Task<ActionResult<IReadOnlyList<object>>> GetUserGroupMembersAsync(
        int ugid,
        CancellationToken cancellationToken)
    {
        if (ugid <= 0)
        {
            return BadRequest(new ProblemDetails { Title = "Invalid user group", Detail = "ugid must be positive." });
        }

        return Ok(await _projectService.GetUserGroupMemberOptionsAsync(ugid, cancellationToken));
    }

    [HttpGet("{id:int}/files")]
    public async Task<ActionResult<IReadOnlyList<ProjectFile>>> ListFilesAsync(
        int id,
        CancellationToken cancellationToken)
    {
        if (id <= 0)
        {
            return BadRequest(new ProblemDetails { Title = "Invalid project", Detail = "id must be positive." });
        }

        try
        {
            return Ok(await _projectService.ListFilesAsync(id, cancellationToken));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Project file list failed for ProjectID {ProjectID}.", id);
            return Problem(
                detail: ex.Message,
                title: "File list failed",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    [HttpPost("{id:int}/files")]
    [RequestFormLimits(MultipartBodyLengthLimit = 52_428_800)]
    [RequestSizeLimit(52_428_800)]
    public async Task<ActionResult<IReadOnlyList<ProjectFile>>> UploadFilesAsync(
        int id,
        [FromForm] List<IFormFile>? files,
        [FromForm] string? category,
        [FromForm] string? description,
        [FromForm] string? versionLabel,
        [FromForm] DateTime? dataAsOf,
        CancellationToken cancellationToken)
    {
        if (id <= 0)
        {
            return BadRequest(new ProblemDetails { Title = "Invalid project", Detail = "id must be positive." });
        }

        var uploads = (files ?? [])
            .Where(f => f is not null && f.Length > 0)
            .ToList();
        if (uploads.Count == 0)
        {
            return BadRequest(new ProblemDetails { Title = "No files", Detail = "Choose one or more files to upload." });
        }

        try
        {
            var streams = new List<(Stream Content, string FileName, string? ContentType, long Length)>();
            var opened = new List<Stream>();
            try
            {
                foreach (var file in uploads)
                {
                    var stream = file.OpenReadStream();
                    opened.Add(stream);
                    streams.Add((stream, file.FileName, file.ContentType, file.Length));
                }

                var saved = await _projectService.UploadFilesAsync(
                    id,
                    streams,
                    string.IsNullOrWhiteSpace(category) ? "SourceData" : category.Trim(),
                    description,
                    versionLabel,
                    dataAsOf,
                    cancellationToken);
                return Ok(saved);
            }
            finally
            {
                foreach (var stream in opened)
                {
                    await stream.DisposeAsync();
                }
            }
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ProblemDetails { Title = "Upload rejected", Detail = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Project file upload failed for ProjectID {ProjectID}.", id);
            return Problem(
                detail: ex.Message,
                title: "File upload failed",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Starts a chunked upload session. Used to stay under IIS requestFiltering limits (~28 MB default).
    /// </summary>
    [HttpPost("{id:int}/files/sessions")]
    public ActionResult<ProjectFileUploadSessionStartResponse> StartFileUploadSession(
        int id,
        [FromBody] ProjectFileUploadSessionStartRequest request)
    {
        if (id <= 0)
        {
            return BadRequest(new ProblemDetails { Title = "Invalid project", Detail = "id must be positive." });
        }

        if (request is null || string.IsNullOrWhiteSpace(request.FileName) || request.ByteLength <= 0)
        {
            return BadRequest(new ProblemDetails { Title = "Invalid file", Detail = "fileName and byteLength are required." });
        }

        if (request.ByteLength > _blobs.MaxUploadBytes)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "File too large",
                Detail = $"File exceeds maximum size ({_blobs.MaxUploadBytes / (1024 * 1024)} MB)."
            });
        }

        var session = _uploadSessions.Create(
            id,
            request.FileName,
            request.ContentType,
            request.ByteLength,
            string.IsNullOrWhiteSpace(request.Category) ? "SourceData" : request.Category.Trim(),
            request.Description,
            request.VersionLabel,
            request.DataAsOf);

        return Ok(new ProjectFileUploadSessionStartResponse
        {
            SessionId = session.SessionId,
            ChunkSizeBytes = _uploadSessions.ChunkSizeBytes,
            MaxUploadBytes = _blobs.MaxUploadBytes
        });
    }

    [HttpPost("{id:int}/files/sessions/{sessionId}/chunks")]
    [RequestFormLimits(MultipartBodyLengthLimit = 12_582_912)]
    [RequestSizeLimit(12_582_912)]
    public async Task<ActionResult> UploadFileChunkAsync(
        int id,
        string sessionId,
        [FromForm] int chunkIndex,
        [FromForm] IFormFile? chunk,
        CancellationToken cancellationToken)
    {
        if (id <= 0 || string.IsNullOrWhiteSpace(sessionId))
        {
            return BadRequest(new ProblemDetails { Title = "Invalid request", Detail = "id and sessionId are required." });
        }

        var session = _uploadSessions.Get(sessionId);
        if (session is null || session.ProjectId != id)
        {
            return NotFound(new ProblemDetails { Title = "Session not found", Detail = "Upload session was not found or expired." });
        }

        if (chunk is null || chunk.Length <= 0)
        {
            return BadRequest(new ProblemDetails { Title = "No chunk", Detail = "chunk file is required." });
        }

        try
        {
            await using var stream = chunk.OpenReadStream();
            await _uploadSessions.SaveChunkAsync(sessionId, chunkIndex, stream, cancellationToken);
            return Ok(new { sessionId, chunkIndex, bytes = chunk.Length });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ProblemDetails { Title = "Chunk rejected", Detail = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Chunk upload failed for ProjectID {ProjectID} session {SessionId}.", id, sessionId);
            return Problem(
                detail: ex.Message,
                title: "Chunk upload failed",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    [HttpPost("{id:int}/files/sessions/{sessionId}/complete")]
    public async Task<ActionResult<IReadOnlyList<ProjectFile>>> CompleteFileUploadSessionAsync(
        int id,
        string sessionId,
        CancellationToken cancellationToken)
    {
        if (id <= 0 || string.IsNullOrWhiteSpace(sessionId))
        {
            return BadRequest(new ProblemDetails { Title = "Invalid request", Detail = "id and sessionId are required." });
        }

        var session = _uploadSessions.Get(sessionId);
        if (session is null || session.ProjectId != id)
        {
            return NotFound(new ProblemDetails { Title = "Session not found", Detail = "Upload session was not found or expired." });
        }

        try
        {
            await using var assembled = await _uploadSessions.OpenAssembledStreamAsync(sessionId, cancellationToken);
            var saved = await _projectService.UploadFilesAsync(
                id,
                [(assembled, session.OriginalFileName, session.ContentType, session.ByteLength)],
                session.Category,
                session.Description,
                session.VersionLabel,
                session.DataAsOf,
                cancellationToken);
            return Ok(saved);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ProblemDetails { Title = "Upload rejected", Detail = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Chunked upload complete failed for ProjectID {ProjectID} session {SessionId}.", id, sessionId);
            return Problem(
                detail: ex.Message,
                title: "File upload failed",
                statusCode: StatusCodes.Status500InternalServerError);
        }
        finally
        {
            _uploadSessions.Delete(sessionId);
        }
    }

    [HttpPut("{id:int}/files/{fileId:int}")]
    [HttpPost("{id:int}/files/{fileId:int}")]
    public async Task<ActionResult<IReadOnlyList<ProjectFile>>> UpdateFileAsync(
        int id,
        int fileId,
        [FromBody] ProjectFileUpdateRequest request,
        CancellationToken cancellationToken)
    {
        if (id <= 0 || fileId <= 0)
        {
            return BadRequest(new ProblemDetails { Title = "Invalid request", Detail = "id and fileId must be positive." });
        }

        try
        {
            var rows = await _projectService.UpdateFileAsync(id, fileId, request ?? new ProjectFileUpdateRequest(), cancellationToken);
            return Ok(rows);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new ProblemDetails { Title = "File not found", Detail = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Project file update failed for ProjectID {ProjectID} FileID {FileID}.", id, fileId);
            return Problem(
                detail: ex.Message,
                title: "File update failed",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    [HttpDelete("{id:int}/files/{fileId:int}")]
    public async Task<ActionResult<object>> DeleteFileAsync(
        int id,
        int fileId,
        CancellationToken cancellationToken)
    {
        if (id <= 0 || fileId <= 0)
        {
            return BadRequest(new ProblemDetails { Title = "Invalid request", Detail = "id and fileId must be positive." });
        }

        try
        {
            await _projectService.SoftDeleteFileAsync(id, fileId, cancellationToken);
            return Ok(new { message = "File removed." });
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new ProblemDetails { Title = "File not found", Detail = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Project file delete failed for ProjectID {ProjectID} FileID {FileID}.", id, fileId);
            return Problem(
                detail: ex.Message,
                title: "File delete failed",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}

/// <summary>Body for <c>PUT/POST /api/projects/{id}/calendar</c>.</summary>
public sealed class ProjectCalendarSaveRequest
{
    public List<ProjectCalendarItem>? Items { get; set; }
    public List<ProjectCalendarItem>? CalendarItems { get; set; }
}

