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

    public ProjectsApiController(IProjectService projectService)
    {
        _projectService = projectService;
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
    public async Task<ActionResult<IReadOnlyList<ActivityLog>>> GetActivityAsync(int id, [FromQuery] int top = 100, CancellationToken cancellationToken = default)
        => Ok(await _projectService.GetActivityAsync(id, top, cancellationToken));

    [HttpGet("{id:int}/calendar")]
    public async Task<ActionResult<IReadOnlyList<ProjectCalendarItem>>> ListCalendarItemsAsync(
        int id,
        CancellationToken cancellationToken)
        => Ok(await _projectService.ListCalendarItemsAsync(id, cancellationToken));

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
}
