using ClientManagementSystem.Models;
using ClientManagementSystem.Services;
using ClientManagementSystem.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace ClientManagementSystem.Api;

[Route("api/clients")]
[ApiController]
public sealed class ClientsApiController : ControllerBase
{
    private readonly IClientService _clientService;
    private readonly ILogger<ClientsApiController> _logger;

    public ClientsApiController(IClientService clientService, ILogger<ClientsApiController> logger)
    {
        _clientService = clientService;
        _logger = logger;
    }

    [HttpGet("form")]
    public async Task<ActionResult<ClientFormViewModel>> GetCreateFormAsync(CancellationToken cancellationToken)
    {
        var model = await _clientService.PrepareCreateFormAsync(cancellationToken: cancellationToken);
        return Ok(model);
    }

    [HttpGet("form/{id:int}")]
    public async Task<ActionResult<ClientFormViewModel>> GetEditFormAsync(int id, CancellationToken cancellationToken)
    {
        var model = await _clientService.PrepareEditFormAsync(id, cancellationToken);
        return model is null ? NotFound() : Ok(model);
    }

    /// <summary>Returns the matching <c>dbo.vClient_WithBU</c> row for the given client id —
    /// used by the Project form Client tab to populate read-only client info.</summary>
    [HttpGet("{id:int}/list-row")]
    public async Task<ActionResult<ClientListRow>> GetListRowAsync(int id, CancellationToken cancellationToken)
    {
        var row = await _clientService.GetClientListRowAsync(id, cancellationToken);
        return row is null ? NotFound() : Ok(row);
    }

    [HttpGet("business-classes")]
    public async Task<ActionResult<IReadOnlyList<object>>> GetBusinessClassesAsync(
        [FromQuery(Name = "businessUnitId")] int businessUnitId,
        CancellationToken cancellationToken)
    {
        var rows = await _clientService.GetBusinessClassOptionsForBusinessUnitAsync(businessUnitId, cancellationToken);
        return Ok(rows.Select(r => new { value = r.Value, text = r.Text }).ToList());
    }

    /// <summary>Directory grid backed by <c>dbo.vClient_WithBU</c> via <c>usp_Client_List</c>.</summary>
    [HttpGet("directory")]
    public async Task<ActionResult<IReadOnlyList<ClientListRow>>> GetDirectoryAsync(
        [FromQuery] string? q = null,
        [FromQuery] int? statusId = null,
        [FromQuery] bool includeDeleted = false,
        CancellationToken cancellationToken = default)
    {
        var clients = await _clientService.ListAsync(q, statusId, includeDeleted, cancellationToken);
        return Ok(clients);
    }

    [HttpPost]
    public async Task<ActionResult<object>> SaveAsync(
        [FromBody] ClientFormViewModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        try
        {
            var clientId = await _clientService.SaveAsync(model, cancellationToken);
            return Ok(new { clientId, message = model.ClientID.HasValue ? "Client updated successfully." : "Client saved successfully." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ProblemDetails { Title = "Cannot save client", Detail = ex.Message });
        }
    }

    [HttpPost("bulk-import/parse")]
    [RequestFormLimits(MultipartBodyLengthLimit = 10_485_760)]
    [RequestSizeLimit(10_485_760)]
    public async Task<ActionResult<ClientBulkParseResult>> ParseBulkImportAsync(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new ProblemDetails { Title = "No file", Detail = "Choose a CSV file to import." });
        }

        try
        {
            await using var stream = file.OpenReadStream();
            var result = await _clientService.ParseImportCsvAsync(stream, cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ProblemDetails { Title = "Parse rejected", Detail = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Client bulk import parse failed.");
            return Problem(
                detail: "The file could not be read. Check the format and try again.",
                title: "Parse failed",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    [HttpPost("bulk-import/verify")]
    public async Task<ActionResult<ClientBulkVerifyResult>> VerifyBulkImportAsync(
        [FromBody] ClientBulkRowsRequest body,
        CancellationToken cancellationToken)
    {
        if (body?.Rows is null)
        {
            return BadRequest(new ProblemDetails { Title = "Invalid body", Detail = "Provide a rows array." });
        }

        var result = await _clientService.VerifyImportRowsAsync(body.Rows, cancellationToken);
        return Ok(result);
    }

    [HttpPost("bulk-import/commit")]
    public async Task<ActionResult<ClientBulkImportResult>> CommitBulkImportAsync(
        [FromBody] ClientBulkRowsRequest body,
        CancellationToken cancellationToken)
    {
        if (body?.Rows is null)
        {
            return BadRequest(new ProblemDetails { Title = "Invalid body", Detail = "Provide a rows array." });
        }

        try
        {
            var result = await _clientService.CommitImportRowsAsync(body.Rows, cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ProblemDetails { Title = "Import not allowed", Detail = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Client bulk import commit failed.");
            return Problem(
                detail: "The import could not be completed. Check database constraints and try again.",
                title: "Import failed",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    [HttpPost("{id:int}/soft-delete")]
    public async Task<ActionResult<object>> SoftDeleteAsync(int id, CancellationToken cancellationToken)
    {
        await _clientService.SoftDeleteAsync(id, cancellationToken);
        return Ok(new { message = "Client soft-deleted." });
    }

    [HttpPost("{id:int}/restore")]
    public async Task<ActionResult<object>> RestoreAsync(int id, CancellationToken cancellationToken)
    {
        await _clientService.RestoreAsync(id, cancellationToken);
        return Ok(new { message = "Client restored." });
    }

    [HttpGet("search")]
    public async Task<IActionResult> SearchAsync(
        [FromQuery] string? q,
        [FromQuery] bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        var clients = await _clientService.ListAsync(q, cancellationToken: cancellationToken);
        return Ok(clients
            .Where(c => !activeOnly || string.Equals(c.StatusCode, "A", StringComparison.OrdinalIgnoreCase))
            .Take(25)
            .Select(c => new
            {
                id = c.ClientID,
                text = string.IsNullOrWhiteSpace(c.ClientShortName) ? c.ClientName : $"{c.ClientName} ({c.ClientShortName})",
                statusCode = c.StatusCode,
                businessUnit = c.BusinessUnitName
            }));
    }
}
