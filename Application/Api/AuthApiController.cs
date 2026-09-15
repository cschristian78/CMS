using System.Security.Claims;
using ClientManagementSystem.Models;
using ClientManagementSystem.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClientManagementSystem.Api;

[Route("api/auth")]
[ApiController]
public sealed class AuthApiController : ControllerBase
{
    private readonly IAuthService _auth;
    private readonly ILogger<AuthApiController> _logger;

    public AuthApiController(IAuthService auth, ILogger<AuthApiController> logger)
    {
        _auth = auth;
        _logger = logger;
    }

    [HttpGet("cat-users")]
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    public async Task<ActionResult> ListCatUsersAsync(CancellationToken cancellationToken)
    {
        try
        {
            var users = await _auth.ListCatUsersAsync(cancellationToken);
            return Ok(new { users });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list CAT login users.");
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                message = "Could not load CAT users for login.",
            });
        }
    }

    [HttpGet("me")]
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    public async Task<ActionResult> MeAsync(CancellationToken cancellationToken)
    {
        if (User?.Identity?.IsAuthenticated != true)
        {
            return Ok(new { authenticated = false });
        }

        var userId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;
        var userType = User.FindFirstValue("userType");
        if (string.IsNullOrWhiteSpace(userType) && userId > 0)
        {
            try
            {
                userType = await _auth.GetUserTypeAsync(userId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not resolve UserType for UserID {UserID}.", userId);
                userType = "Basic";
            }
        }

        return Ok(new
        {
            authenticated = true,
            userID = userId,
            email = User.FindFirstValue(ClaimTypes.Email) ?? string.Empty,
            fullName = User.FindFirstValue(ClaimTypes.Name) ?? string.Empty,
            userType = string.IsNullOrWhiteSpace(userType) ? "Basic" : userType.Trim(),
        });
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult> LoginAsync([FromBody] AuthLoginRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var session = await _auth.LoginAsync(request ?? new AuthLoginRequest(), cancellationToken);
            await SignInAsync(session);
            return Ok(new
            {
                message = "Signed in.",
                userID = session.UserID,
                email = session.Email,
                fullName = session.FullName,
                userType = session.UserType,
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Login failed.");
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                message = "Sign-in failed.",
            });
        }
    }

    [HttpPost("logout")]
    public async Task<ActionResult> LogoutAsync()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Ok(new { message = "Signed out." });
    }

    private async Task SignInAsync(AuthSessionDto session)
    {
        var userType = string.IsNullOrWhiteSpace(session.UserType) ? "Basic" : session.UserType.Trim();
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, session.UserID.ToString()),
            new(ClaimTypes.Email, session.Email),
            new(ClaimTypes.Name, session.FullName),
            new("department", "CAT"),
            new("userType", userType),
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = true,
                AllowRefresh = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(12),
            });
    }
}
