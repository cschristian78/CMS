using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;

namespace ClientManagementSystem.Api;

[ApiController]
[Route("api")]
public sealed class AntiforgeryController : ControllerBase
{
    [HttpGet("antiforgery-token")]
    [IgnoreAntiforgeryToken]
    public IActionResult GetToken([FromServices] IAntiforgery antiforgery)
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        return Ok(new { token = tokens.RequestToken });
    }
}
