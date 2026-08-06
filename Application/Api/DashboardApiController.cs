using ClientManagementSystem.Models;
using ClientManagementSystem.Services;
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

    [HttpGet("clients-by-business-unit")]
    public async Task<ActionResult<IReadOnlyList<DashboardBusinessUnitCount>>> GetClientsByBusinessUnitAsync(
        [FromQuery] string? analyticsLeadName = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var rows = await _dashboardService.GetClientCountByBusinessUnitAsync(
                analyticsLeadName,
                cancellationToken);
            return Ok(rows);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dashboard clients-by-business-unit failed.");
            return Problem(
                detail: "Could not load client counts by business unit.",
                title: "Operation failed",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    [HttpGet("projects-by-business-unit")]
    public async Task<ActionResult<IReadOnlyList<DashboardBusinessUnitCount>>> GetProjectsByBusinessUnitAsync(
        [FromQuery] string? analyticsLeadName = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var rows = await _dashboardService.GetProjectCountByBusinessUnitAsync(
                analyticsLeadName,
                cancellationToken);
            return Ok(rows);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dashboard projects-by-business-unit failed.");
            return Problem(
                detail: "Could not load project counts by business unit.",
                title: "Operation failed",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    [HttpGet("clients-by-business-class")]
    public async Task<ActionResult<IReadOnlyList<DashboardNamedCount>>> GetClientsByBusinessClassAsync(
        [FromQuery] string? analyticsLeadName = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var rows = await _dashboardService.GetClientCountByBusinessClassAsync(
                analyticsLeadName,
                cancellationToken);
            return Ok(rows);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dashboard clients-by-business-class failed.");
            return Problem(
                detail: "Could not load client counts by business class.",
                title: "Operation failed",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    [HttpGet("projects-by-business-class")]
    public async Task<ActionResult<IReadOnlyList<DashboardNamedCount>>> GetProjectsByBusinessClassAsync(
        [FromQuery] string? analyticsLeadName = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var rows = await _dashboardService.GetProjectCountByBusinessClassAsync(
                analyticsLeadName,
                cancellationToken);
            return Ok(rows);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dashboard projects-by-business-class failed.");
            return Problem(
                detail: "Could not load project counts by business class.",
                title: "Operation failed",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
