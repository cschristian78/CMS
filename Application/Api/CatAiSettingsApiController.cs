using System.Security.Claims;
using ClientManagementSystem.Models;
using ClientManagementSystem.Security;
using ClientManagementSystem.Services;
using Microsoft.AspNetCore.Mvc;

namespace ClientManagementSystem.Api;

[Route("api/admin/catai-settings")]
[ApiController]
public sealed class CatAiSettingsApiController : ControllerBase
{
    private readonly ICatAiSettingsResolver _resolver;
    private readonly ICatAiSettingsStore _store;
    private readonly ICatAiChatService _chat;
    private readonly IAuthService _auth;
    private readonly ILogger<CatAiSettingsApiController> _logger;

    public CatAiSettingsApiController(
        ICatAiSettingsResolver resolver,
        ICatAiSettingsStore store,
        ICatAiChatService chat,
        IAuthService auth,
        ILogger<CatAiSettingsApiController> logger)
    {
        _resolver = resolver;
        _store = store;
        _chat = chat;
        _auth = auth;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<CatAiSettingsDto>> GetAsync(CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out _))
        {
            return Unauthorized();
        }

        if (!await IsAdministratorAsync(cancellationToken))
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        var settings = await _resolver.ResolveAsync(cancellationToken);
        return Ok(ToDto(settings));
    }

    [HttpPut]
    public async Task<ActionResult<CatAiSettingsDto>> PutAsync(
        [FromBody] CatAiSettingsDto body,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out _))
        {
            return Unauthorized();
        }

        if (!await IsAdministratorAsync(cancellationToken))
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        if (body is null)
        {
            return BadRequest(new ProblemDetails { Title = "Invalid request", Detail = "Body is required." });
        }

        var updateApiKey = !string.IsNullOrWhiteSpace(body.ApiKey)
            && !LooksMasked(body.ApiKey);

        var settings = new CatAiSettings
        {
            ApiKey = updateApiKey ? body.ApiKey!.Trim() : string.Empty,
            Model = body.Model?.Trim() ?? string.Empty,
            BaseUrl = body.BaseUrl?.Trim() ?? string.Empty,
            Deployment = body.Deployment?.Trim() ?? string.Empty,
            ApiVersion = body.ApiVersion?.Trim() ?? string.Empty,
            SystemPrompt = string.IsNullOrWhiteSpace(body.SystemPrompt)
                ? CatAiDefaults.SystemPrompt
                : body.SystemPrompt.Trim(),
            Temperature = Math.Clamp(body.Temperature, 0, 2),
            MaxTokens = Math.Clamp(body.MaxTokens <= 0 ? 1500 : body.MaxTokens, 64, 8000),
            MaxToolRounds = Math.Clamp(body.MaxToolRounds <= 0 ? 5 : body.MaxToolRounds, 1, 10),
        };

        await _store.SaveOverrideAsync(settings, updateApiKey, cancellationToken);
        var resolved = await _resolver.ResolveAsync(cancellationToken);
        return Ok(ToDto(resolved));
    }

    [HttpPost("test")]
    public async Task<ActionResult<ConnectionTestResult>> TestAsync(CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out _))
        {
            return Unauthorized();
        }

        if (!await IsAdministratorAsync(cancellationToken))
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        try
        {
            return Ok(await _chat.TestConnectionAsync(cancellationToken));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "CatAI settings test failed.");
            return Ok(ConnectionTestResult.Failure(ex.Message));
        }
    }

    private static CatAiSettingsDto ToDto(CatAiSettings settings) => new()
    {
        ApiKey = string.IsNullOrWhiteSpace(settings.ApiKey) ? string.Empty : MaskKey(settings.ApiKey),
        ApiKeyConfigured = !string.IsNullOrWhiteSpace(settings.ApiKey),
        Model = settings.Model,
        BaseUrl = settings.BaseUrl,
        Deployment = settings.Deployment,
        ApiVersion = settings.ApiVersion,
        SystemPrompt = settings.SystemPrompt,
        Temperature = settings.Temperature,
        MaxTokens = settings.MaxTokens,
        MaxToolRounds = settings.MaxToolRounds,
    };

    private static string MaskKey(string apiKey)
    {
        if (apiKey.Length <= 8)
        {
            return new string('•', apiKey.Length);
        }

        return apiKey[..4] + new string('•', Math.Min(24, apiKey.Length - 8)) + apiKey[^4..];
    }

    private static bool LooksMasked(string value)
    {
        return value.Contains('•', StringComparison.Ordinal) || value.Contains('*', StringComparison.Ordinal);
    }

    private async Task<bool> IsAdministratorAsync(CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return false;
        }

        var userType = User.FindFirstValue("userType");
        if (string.IsNullOrWhiteSpace(userType))
        {
            userType = await _auth.GetUserTypeAsync(userId, cancellationToken);
        }

        return string.Equals(userType, "Administrator", StringComparison.OrdinalIgnoreCase);
    }

    private bool TryGetUserId(out int userId)
    {
        userId = 0;
        return int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId) && userId > 0;
    }
}
