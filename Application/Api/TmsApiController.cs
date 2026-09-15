using System.Security.Claims;
using ClientManagementSystem.Models;
using ClientManagementSystem.Services;
using Microsoft.AspNetCore.Mvc;

namespace ClientManagementSystem.Api;

[Route("api/tms")]
[ApiController]
public sealed class TmsApiController : ControllerBase
{
    private readonly ITmsService _tms;
    private readonly IAuthService _auth;
    private readonly ILogger<TmsApiController> _logger;

    public TmsApiController(ITmsService tms, IAuthService auth, ILogger<TmsApiController> logger)
    {
        _tms = tms;
        _auth = auth;
        _logger = logger;
    }

    [HttpGet("cat-users")]
    public async Task<ActionResult<IReadOnlyList<TmsCatUser>>> ListCatUsersAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _tms.ListCatUsersAsync(cancellationToken));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TMS CAT user list failed.");
            return Problem(
                detail: ex.Message,
                title: "CAT user list failed",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    [HttpGet("report")]
    public async Task<ActionResult<TmsReportResponse>> GetReportAsync(
        [FromQuery] int? year,
        [FromQuery] int? userId,
        CancellationToken cancellationToken)
    {
        var reportYear = year is >= 2000 and <= 2100 ? year.Value : DateTime.Today.Year;
        try
        {
            return Ok(await _tms.GetReportAsync(reportYear, userId is > 0 ? userId : null, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ProblemDetails { Title = "TMS report rejected", Detail = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TMS report failed for year {Year}, user {UserID}.", reportYear, userId);
            return Problem(
                detail: ex.Message,
                title: "TMS report failed",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    [HttpGet("week")]
    public async Task<ActionResult<TmsWeekResponse>> GetWeekAsync(
        [FromQuery] int userId,
        [FromQuery] DateTime? weekStart,
        CancellationToken cancellationToken)
    {
        if (userId <= 0)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid user",
                Detail = "userId must be a positive CAT user id.",
            });
        }

        try
        {
            userId = await ResolveRequestedUserIdAsync(userId, cancellationToken);
            var monday = (weekStart ?? DateTime.Today).Date;
            return Ok(await _tms.GetWeekAsync(userId, monday, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ProblemDetails { Title = "TMS week rejected", Detail = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TMS week load failed for UserID {UserID}.", userId);
            return Problem(
                detail: ex.Message,
                title: "TMS week load failed",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    [HttpPost("week")]
    [HttpPut("week")]
    public async Task<ActionResult<TmsWeekSaveResult>> SaveWeekAsync(
        [FromBody] TmsWeekSaveRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null || request.UserID <= 0)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid request",
                Detail = "userID is required.",
            });
        }

        try
        {
            request.UserID = await ResolveRequestedUserIdAsync(request.UserID, cancellationToken);
            return Ok(await _tms.SaveWeekAsync(request, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ProblemDetails { Title = "TMS save rejected", Detail = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TMS week save failed for UserID {UserID}.", request.UserID);
            return Problem(
                detail: ex.Message,
                title: "TMS week save failed",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private async Task<int> ResolveRequestedUserIdAsync(int requestedUserId, CancellationToken cancellationToken)
    {
        var actingUserId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;
        if (await IsAdministratorAsync(actingUserId, cancellationToken))
        {
            return requestedUserId;
        }

        if (actingUserId <= 0)
        {
            throw new InvalidOperationException("Sign in is required to load or save time.");
        }

        return actingUserId;
    }

    private async Task<bool> IsAdministratorAsync(int actingUserId, CancellationToken cancellationToken)
    {
        var userType = User.FindFirstValue("userType");
        if (string.IsNullOrWhiteSpace(userType) && actingUserId > 0)
        {
            userType = await _auth.GetUserTypeAsync(actingUserId, cancellationToken);
        }

        return string.Equals(userType, "Administrator", StringComparison.OrdinalIgnoreCase);
    }
}
