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

    [HttpGet("clients-by-business-unit")]
    public async Task<ActionResult<IReadOnlyList<DashboardBusinessUnitCount>>> GetClientsByBusinessUnitAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var rows = await _dashboardService.GetClientCountByBusinessUnitAsync(cancellationToken);
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
        CancellationToken cancellationToken)
    {
        try
        {
            var rows = await _dashboardService.GetProjectCountByBusinessUnitAsync(cancellationToken);
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
        CancellationToken cancellationToken)
    {
        try
        {
            var rows = await _dashboardService.GetClientCountByBusinessClassAsync(cancellationToken);
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
        CancellationToken cancellationToken)
    {
        try
        {
            var rows = await _dashboardService.GetProjectCountByBusinessClassAsync(cancellationToken);
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
