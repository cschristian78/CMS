using ClientManagementSystem.Models;
using ClientManagementSystem.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ClientManagementSystem.Api;

[Route("api/app")]
[ApiController]
public sealed class AppApiController : ControllerBase
{
    private const string HelpManualBlobPath = "Help/CMS_Analyst_Training_Manual.pdf";

    [HttpGet("config")]
    public ActionResult<object> GetConfig([FromServices] IOptions<EmailOptions> emailOptions)
    {
        var publicBaseUrl = (emailOptions.Value.PublicBaseUrl ?? string.Empty).Trim().TrimEnd('/');
        return Ok(new { publicBaseUrl });
    }

    /// <summary>
    /// Opens the analyst training manual from Azure Blob (storage account is not public).
    /// </summary>
    [HttpGet("help-manual")]
    [IgnoreAntiforgeryToken]
    public ActionResult OpenHelpManual([FromServices] IProjectBlobStorage blobs)
    {
        var url = blobs.BuildDownloadUrl(HelpManualBlobPath);
        if (string.IsNullOrWhiteSpace(url))
        {
            return NotFound(new { message = "Help manual is not configured." });
        }

        return Redirect(url);
    }
}
