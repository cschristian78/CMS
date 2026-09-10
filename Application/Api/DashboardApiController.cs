using ClientManagementSystem.Models;
using ClientManagementSystem.Services;
using ClientManagementSystem.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace ClientManagementSystem.Api;

[Route("api/dashboard")]
[ApiController]
public sealed class DashboardApiController : ControllerBase
{
    private readonly IDashboardService _dashboardService;
    private readonly ILogger<DashboardApiController> _logger;

    public DashboardApiController(IDashboardService dashboardService, ILogger<DashboardApiController> logger)
    {
        _dashboardService = dashboardService;
        _logger = logger;
    }

    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [HttpGet("filter-options")]
    public async Task<ActionResult<object>> GetFilterOptionsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var analyticsLeadNames = await _dashboardService.GetAnalyticsLeadFilterOptionsAsync(cancellationToken);
            return Ok(new { analyticsLeadNames });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dashboard filter-options failed.");
            return Problem(
                detail: "Could not load dashboard filter options (Analytics Lead).",
                title: "Operation failed",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [HttpGet("dataset")]
    public async Task<ActionResult<DashboardDatasetViewModel>> GetDatasetAsync(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _dashboardService.GetDatasetAsync(cancellationToken));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dashboard dataset failed.");
            var root = ex.GetBaseException();
            return Problem(
                detail: $"Could not load dashboard dataset from dbo.vProject_Current. {root.GetType().Name}: {root.Message}",
                title: "Operation failed",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Bar-chart series: distinct project counts by begin month (<c>tProject.CreatedDate</c>).
    /// Query: <c>analyticsLeadName</c>, <c>onlineStatus</c>=Online|All.
    /// </summary>
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [HttpGet("projects-by-begin-month")]
    public async Task<ActionResult<IReadOnlyList<DashboardNamedCount>>> GetProjectsByBeginMonthAsync(
        [FromQuery] string? analyticsLeadName = null,
        [FromQuery] string? onlineStatus = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var rows = await _dashboardService.GetProjectCountByBeginMonthAsync(
                analyticsLeadName,
                ParseOnlineStatus(onlineStatus),
                cancellationToken);
            return Ok(rows);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dashboard projects-by-begin-month failed.");
            var root = ex.GetBaseException();
            return Problem(
                detail: $"Could not load project counts by begin month. {root.GetType().Name}: {root.Message}",
                title: "Operation failed",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [HttpGet("workload/cat-staff")]
    public async Task<ActionResult<IReadOnlyList<CatStaffHoursRow>>> GetCatStaffHoursAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _dashboardService.GetCatStaffHoursAsync(cancellationToken));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dashboard CAT staff hours failed.");
            var root = ex.GetBaseException();
            return Problem(
                detail: $"Could not load CAT department hours. {root.GetType().Name}: {root.Message}",
                title: "Operation failed",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [HttpGet("workload/cat-staff/{userId:int}")]
    public async Task<ActionResult<CatAnalystWorkloadDetail>> GetCatAnalystWorkloadDetailAsync(
        int userId,
        CancellationToken cancellationToken)
    {
        try
        {
            var detail = await _dashboardService.GetCatAnalystWorkloadDetailAsync(userId, cancellationToken);
            if (detail is null)
            {
                return NotFound();
            }

            return Ok(detail);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dashboard CAT analyst detail failed for UserID {UserId}.", userId);
            var root = ex.GetBaseException();
            return Problem(
                detail: $"Could not load CAT analyst hours summary. {root.GetType().Name}: {root.Message}",
                title: "Operation failed",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>Online → true; All / empty / unknown → null (no IsOnline filter).</summary>
    private static bool? ParseOnlineStatus(string? onlineStatus)
    {
        if (string.IsNullOrWhiteSpace(onlineStatus))
        {
            return null;
        }

        var value = onlineStatus.Trim();
        if (value.Equals("Online", StringComparison.OrdinalIgnoreCase)
            || value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("1", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // All (and offline-as-all for this dashboard) → no filter
        return null;
    }
}
