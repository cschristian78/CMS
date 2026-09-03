using ClientManagementSystem.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ClientManagementSystem.Api;

[Route("api/app")]
[ApiController]
public sealed class AppApiController : ControllerBase
{
    [HttpGet("config")]
    public ActionResult<object> GetConfig([FromServices] IOptions<EmailOptions> emailOptions)
    {
        var publicBaseUrl = (emailOptions.Value.PublicBaseUrl ?? string.Empty).Trim().TrimEnd('/');
        return Ok(new { publicBaseUrl });
    }
}
