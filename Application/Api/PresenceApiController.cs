using System.Security.Claims;
using ClientManagementSystem.Models;
using ClientManagementSystem.Services;
using Microsoft.AspNetCore.Mvc;

namespace ClientManagementSystem.Api;

[Route("api/presence")]
[ApiController]
public sealed class PresenceApiController : ControllerBase
{
    /// <summary>Users are considered online if they heartbeated within this window.</summary>
    private static readonly TimeSpan ActiveWindow = TimeSpan.FromMinutes(2);

    private readonly IPresenceTracker _presence;
    private readonly IUserPhotoStorage _photos;

    public PresenceApiController(IPresenceTracker presence, IUserPhotoStorage photos)
    {
        _presence = presence;
        _photos = photos;
    }

    [HttpPost("heartbeat")]
    public ActionResult Heartbeat()
    {
        if (!TryGetUser(out var userId, out var fullName, out var email))
        {
            return Unauthorized();
        }

        var hasPhoto = _photos.TryGetPhotoFile(userId, out _, out _);
        _presence.Touch(userId, fullName, email, hasPhoto);
        return Ok(new PresenceSnapshotDto { Users = SnapshotExcluding(userId) });
    }

    [HttpGet]
    public ActionResult GetActive()
    {
        if (!TryGetUser(out var userId, out _, out _))
        {
            return Unauthorized();
        }

        return Ok(new PresenceSnapshotDto { Users = SnapshotExcluding(userId) });
    }

    [HttpPost("leave")]
    public ActionResult Leave()
    {
        if (!TryGetUser(out var userId, out _, out _))
        {
            return Unauthorized();
        }

        _presence.Leave(userId);
        return Ok(new { message = "Left." });
    }

    private IReadOnlyList<PresenceUserDto> SnapshotExcluding(int currentUserId)
        => _presence.GetActive(ActiveWindow).Where(u => u.UserID != currentUserId).ToList();

    private bool TryGetUser(out int userId, out string fullName, out string email)
    {
        userId = 0;
        fullName = string.Empty;
        email = string.Empty;

        if (User?.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId) || userId <= 0)
        {
            return false;
        }

        fullName = User.FindFirstValue(ClaimTypes.Name) ?? string.Empty;
        email = User.FindFirstValue(ClaimTypes.Email) ?? string.Empty;
        return true;
    }
}
