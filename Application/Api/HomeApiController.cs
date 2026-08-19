using ClientManagementSystem.Models;
using ClientManagementSystem.Services;
using ClientManagementSystem.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace ClientManagementSystem.Api;

[Route("api/home")]
[ApiController]
public sealed class HomeApiController : ControllerBase
{
    private readonly IHomeService _homeService;
    private readonly ILogger<HomeApiController> _logger;

    public HomeApiController(IHomeService homeService, ILogger<HomeApiController> logger)
    {
        _homeService = homeService;
        _logger = logger;
    }

    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [HttpGet("summary")]
    public async Task<ActionResult<HomeSummary>> GetSummaryAsync(
        [FromQuery] string? businessUnitName = null,
        [FromQuery] string? statusName = null,
        [FromQuery] string? clientName = null,
        [FromQuery] string? businessClassName = null,
        [FromQuery] string? projectTypeName = null,
        [FromQuery] string? onlineStatus = null,
        [FromQuery] string[]? analyticsLeadName = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var summary = await _homeService.GetHomeSummaryAsync(
                businessUnitName,
                statusName,
                clientName,
                businessClassName,
                projectTypeName,
                ParseOnlineStatus(onlineStatus),
                NormalizeAnalyticsLeadNames(analyticsLeadName),
                cancellationToken);
            return Ok(summary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Home API summary failed.");
            return Problem(
                detail: "Could not load home summary statistics.",
                title: "Operation failed",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [HttpGet("client-project-list/options")]
    public async Task<ActionResult<ClientProjectListFilterOptionsViewModel>> GetClientProjectListOptionsAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var model = await _homeService.GetHomeClientProjectListFilterOptionsAsync(cancellationToken);
            return Ok(model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Home API client-project-list options failed.");
            var diagnostic = BuildTemporaryDiagnosticDetail(ex);
            return Problem(
                detail: $"Could not load business unit (BUName), business class (BCName), status (StatusDescription), client name, or project type (PTDescription) filter options from dbo.vProject_Current. TEMP DIAGNOSTIC: {diagnostic}",
                title: "Operation failed",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [HttpGet("client-project-list")]
    public async Task<ActionResult<ClientProjectListViewModel>> GetClientProjectListAsync(
        [FromQuery] bool refresh = true,
        [FromQuery] string? businessUnitName = null,
        [FromQuery] string? statusName = null,
        [FromQuery] string? clientName = null,
        [FromQuery] string? businessClassName = null,
        [FromQuery] string? projectTypeName = null,
        [FromQuery] string? onlineStatus = null,
        [FromQuery] string[]? analyticsLeadName = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var model = await _homeService.GetHomeClientProjectListAsync(
                refresh,
                businessUnitName,
                statusName,
                clientName,
                businessClassName,
                projectTypeName,
                ParseOnlineStatus(onlineStatus),
                NormalizeAnalyticsLeadNames(analyticsLeadName),
                cancellationToken);
            return Ok(model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Home API client-project-list failed.");
            var diagnostic = BuildTemporaryDiagnosticDetail(ex);
            return Problem(
                detail: $"Could not load the client/project list from dbo.vProject_Current. TEMP DIAGNOSTIC: {diagnostic}",
                title: "Operation failed",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    [HttpPost("client-project/bulk-import/parse")]
    [RequestFormLimits(MultipartBodyLengthLimit = 10_485_760)]
    [RequestSizeLimit(10_485_760)]
    public async Task<ActionResult<HomeClientProjectBulkParseResult>> ParseClientProjectBulkImportAsync(
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
            return Ok(await _homeService.ParseClientProjectImportCsvAsync(stream, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ProblemDetails { Title = "Parse rejected", Detail = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Home client-project bulk import parse failed.");
            return Problem(detail: "Parse failed.", title: "Parse failed", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    [HttpPost("client-project/bulk-import/verify")]
    public async Task<ActionResult<HomeClientProjectBulkVerifyResult>> VerifyClientProjectBulkImportAsync(
        [FromBody] HomeClientProjectBulkRowsRequest body,
        CancellationToken cancellationToken)
    {
        if (body?.Rows is null)
        {
            return BadRequest(new ProblemDetails { Title = "Invalid body", Detail = "Provide a rows array." });
        }

        return Ok(await _homeService.VerifyClientProjectImportRowsAsync(body.Rows, cancellationToken));
    }

    [HttpPost("client-project/bulk-import/commit")]
    public async Task<ActionResult<HomeClientProjectBulkImportResult>> CommitClientProjectBulkImportAsync(
        [FromBody] HomeClientProjectBulkRowsRequest body,
        CancellationToken cancellationToken)
    {
        if (body?.Rows is null)
        {
            return BadRequest(new ProblemDetails { Title = "Invalid body", Detail = "Provide a rows array." });
        }

        try
        {
            return Ok(await _homeService.CommitClientProjectImportRowsAsync(body.Rows, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ProblemDetails { Title = "Import rejected", Detail = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Home client-project bulk import commit failed.");
            return Problem(detail: "Import failed.", title: "Import failed", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static IReadOnlyList<string>? NormalizeAnalyticsLeadNames(string[]? analyticsLeadName)
    {
        if (analyticsLeadName is null || analyticsLeadName.Length == 0)
        {
            return null;
        }

        var names = analyticsLeadName
            .SelectMany(s => (s ?? string.Empty).Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return names.Count == 0 ? null : names;
    }

    private static bool? ParseOnlineStatus(string? onlineStatus)
    {
        if (string.IsNullOrWhiteSpace(onlineStatus))
        {
            return null;
        }

        var value = onlineStatus.Trim();
        if (value.Equals("Online", StringComparison.OrdinalIgnoreCase)
            || value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("1", StringComparison.OrdinalIgnoreCase)
            || value.Equals("yes", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (value.Equals("Offline", StringComparison.OrdinalIgnoreCase)
            || value.Equals("false", StringComparison.OrdinalIgnoreCase)
            || value.Equals("0", StringComparison.OrdinalIgnoreCase)
            || value.Equals("no", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return null;
    }

    private static string BuildTemporaryDiagnosticDetail(Exception ex)
    {
        var root = ex.GetBaseException();
        return string.Equals(root.Message, ex.Message, StringComparison.Ordinal)
            ? ex.Message
            : $"{ex.Message} | Root: {root.Message}";
    }
}
