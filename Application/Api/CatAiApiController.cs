using System.Security.Claims;
using ClientManagementSystem.Data;
using ClientManagementSystem.Models;
using ClientManagementSystem.Services;
using Microsoft.AspNetCore.Mvc;

namespace ClientManagementSystem.Api;

[Route("api/catai")]
[ApiController]
public sealed class CatAiApiController : ControllerBase
{
    private readonly ICatAiChatService _chat;
    private readonly ICatAiConversationRepository _conversations;
    private readonly IAuthService _auth;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<CatAiApiController> _logger;

    public CatAiApiController(
        ICatAiChatService chat,
        ICatAiConversationRepository conversations,
        IAuthService auth,
        IHostEnvironment environment,
        ILogger<CatAiApiController> logger)
    {
        _chat = chat;
        _conversations = conversations;
        _auth = auth;
        _environment = environment;
        _logger = logger;
    }

    [HttpPost("chat")]
    public async Task<ActionResult<CatAiChatResult>> ChatAsync(
        [FromBody] CatAiChatRequest body,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        if (body.Messages is null || body.Messages.Count == 0)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid request",
                Detail = "Provide at least one message.",
            });
        }

        var lastUser = body.Messages.LastOrDefault(m =>
            string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase));
        if (lastUser is null || string.IsNullOrWhiteSpace(lastUser.Content))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid request",
                Detail = "Provide a user message.",
            });
        }

        try
        {
            var conversationId = body.ConversationId;
            if (conversationId is null or <= 0)
            {
                var title = BuildTitle(lastUser.Content);
                conversationId = await _conversations.CreateAsync(userId, title, cancellationToken);
            }
            else
            {
                var existing = await _conversations.GetAsync(conversationId.Value, userId, cancellationToken);
                if (existing is null)
                {
                    return NotFound(new ProblemDetails
                    {
                        Title = "Not found",
                        Detail = "Conversation was not found.",
                    });
                }

                await _conversations.TouchAsync(conversationId.Value, userId, BuildTitle(lastUser.Content), cancellationToken);
            }

            await _conversations.AppendMessageAsync(
                conversationId.Value,
                userId,
                "user",
                lastUser.Content.Trim(),
                null,
                cancellationToken);

            var (wizardActive, wizardKind) = await _conversations.GetWizardAsync(
                conversationId.Value,
                userId,
                cancellationToken);

            var userType = await _auth.GetUserTypeAsync(userId, cancellationToken);
            var actor = new CatAiActorContext
            {
                UserId = userId,
                UserType = string.IsNullOrWhiteSpace(userType) ? "Basic" : userType.Trim(),
            };

            // Heuristic: begin wizard on clear create intents even before the start_* tool runs.
            if (!wizardActive)
            {
                var inferred = InferWizardKind(lastUser.Content);
                if (inferred is not null && CatAiToolPermissions.CanStartCreateWizard(actor))
                {
                    wizardKind = inferred;
                    wizardActive = true;
                    await _conversations.SetWizardAsync(conversationId.Value, userId, inferred, true, cancellationToken);
                }
                else if (inferred is not null && !CatAiToolPermissions.CanStartCreateWizard(actor))
                {
                    // Do not enter create wizards for Analyst / read-only user types.
                    wizardKind = null;
                    wizardActive = false;
                }
            }
            else if (!CatAiToolPermissions.CanStartCreateWizard(actor))
            {
                // Drop an active create wizard if the caller's role cannot manage creates.
                await _conversations.SetWizardAsync(conversationId.Value, userId, null, false, cancellationToken);
                wizardActive = false;
                wizardKind = null;
            }

            // Default: keep conversation memory. Use New chat in the UI to start a fresh thread.
            var detail = await _conversations.GetAsync(conversationId.Value, userId, cancellationToken);
            IReadOnlyList<CatAiChatMessageDto> modelMessages = (detail?.Messages ?? [])
                .Where(m =>
                    string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(m.Role, "assistant", StringComparison.OrdinalIgnoreCase))
                .Select(m => new CatAiChatMessageDto
                {
                    Role = m.Role.Trim().ToLowerInvariant(),
                    Content = m.Content,
                })
                .ToList();

            var result = await _chat.ChatAsync(
                modelMessages,
                wizardActive ? wizardKind : null,
                actor,
                cancellationToken);

            foreach (var toolName in result.ToolsUsed)
            {
                await _conversations.AppendMessageAsync(
                    conversationId.Value,
                    userId,
                    "tool",
                    $"Used tool: {toolName}",
                    toolName,
                    cancellationToken);
            }

            await ApplyWizardTransitionsAsync(
                conversationId.Value,
                userId,
                result.ToolsUsed,
                cancellationToken);

            var (activeAfter, kindAfter) = await _conversations.GetWizardAsync(
                conversationId.Value,
                userId,
                cancellationToken);

            await _conversations.AppendMessageAsync(
                conversationId.Value,
                userId,
                "assistant",
                result.Reply,
                null,
                cancellationToken);

            result.ConversationId = conversationId;
            result.WizardActive = activeAfter;
            result.WizardKind = kindAfter;
            result.WizardLabel = WizardLabel(kindAfter, activeAfter);
            return Ok(result);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not configured", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(ex, "CatAI configuration missing.");
            return Problem(
                detail: ex.Message,
                title: "Configuration missing",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "CatAI chat failed.");
            return Problem(
                detail: ex.Message,
                title: "CatAI unavailable",
                statusCode: StatusCodes.Status502BadGateway);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CatAI chat failed.");
            return Problem(
                detail: _environment.IsDevelopment()
                    ? ex.Message
                    : "The AI request could not be completed. Please try again.",
                title: "CatAI unavailable",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    [HttpGet("conversations")]
    public async Task<ActionResult<IReadOnlyList<CatAiConversationSummary>>> ListConversationsAsync(
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        return Ok(await _conversations.ListAsync(userId, cancellationToken));
    }

    [HttpPost("conversations")]
    public async Task<ActionResult<object>> CreateConversationAsync(
        [FromBody] CatAiCreateConversationRequest? body,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var id = await _conversations.CreateAsync(userId, body?.Title ?? "New chat", cancellationToken);
        return Ok(new { conversationId = id });
    }

    [HttpGet("conversations/{id:int}")]
    public async Task<ActionResult<CatAiConversationDetail>> GetConversationAsync(
        int id,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var detail = await _conversations.GetAsync(id, userId, cancellationToken);
        if (detail is null)
        {
            return NotFound();
        }

        return Ok(detail);
    }

    [HttpDelete("conversations/{id:int}")]
    public async Task<ActionResult> DeleteConversationAsync(int id, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        await _conversations.SoftDeleteAsync(id, userId, cancellationToken);
        return Ok(new { message = "Conversation deleted." });
    }

    [HttpPost("conversations/{id:int}/cancel-wizard")]
    public async Task<ActionResult<object>> CancelWizardAsync(int id, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var detail = await _conversations.GetAsync(id, userId, cancellationToken);
        if (detail is null)
        {
            return NotFound();
        }

        await _conversations.SetWizardAsync(id, userId, null, false, cancellationToken);
        return Ok(new { wizardActive = false, message = "Wizard cancelled." });
    }

    private async Task ApplyWizardTransitionsAsync(
        int conversationId,
        int userId,
        IReadOnlyList<string> toolsUsed,
        CancellationToken cancellationToken)
    {
        if (toolsUsed.Count == 0)
        {
            return;
        }

        if (toolsUsed.Any(t => string.Equals(t, "cancel_wizard", StringComparison.OrdinalIgnoreCase)))
        {
            await _conversations.SetWizardAsync(conversationId, userId, null, false, cancellationToken);
            return;
        }

        if (toolsUsed.Any(t => string.Equals(t, "start_renewal_project_wizard", StringComparison.OrdinalIgnoreCase)))
        {
            await _conversations.SetWizardAsync(
                conversationId,
                userId,
                CatAiDefaults.RenewalProjectWizardKind,
                true,
                cancellationToken);
        }
        else if (toolsUsed.Any(t => string.Equals(t, "start_user_group_wizard", StringComparison.OrdinalIgnoreCase)))
        {
            await _conversations.SetWizardAsync(
                conversationId,
                userId,
                CatAiDefaults.UserGroupWizardKind,
                true,
                cancellationToken);
        }

        if (toolsUsed.Any(t =>
                string.Equals(t, "create_renewal_project", StringComparison.OrdinalIgnoreCase)
                || string.Equals(t, "create_user_group", StringComparison.OrdinalIgnoreCase)))
        {
            await _conversations.SetWizardAsync(conversationId, userId, null, false, cancellationToken);
        }
    }

    private static string? InferWizardKind(string content)
    {
        var t = content.Trim().ToLowerInvariant();
        if (t.Length == 0)
        {
            return null;
        }

        var wantsCreate = t.Contains("create", StringComparison.Ordinal)
            || t.Contains("new ", StringComparison.Ordinal)
            || t.Contains("set up", StringComparison.Ordinal)
            || t.Contains("setup", StringComparison.Ordinal);

        if (!wantsCreate)
        {
            return null;
        }

        if (t.Contains("user group", StringComparison.Ordinal) || t.Contains("usergroup", StringComparison.Ordinal))
        {
            return CatAiDefaults.UserGroupWizardKind;
        }

        if (t.Contains("renewal project", StringComparison.Ordinal)
            || (t.Contains("renewal", StringComparison.Ordinal) && t.Contains("project", StringComparison.Ordinal))
            || t.Contains("rnwl", StringComparison.Ordinal))
        {
            return CatAiDefaults.RenewalProjectWizardKind;
        }

        return null;
    }

    private static string? WizardLabel(string? kind, bool active)
    {
        if (!active || string.IsNullOrWhiteSpace(kind))
        {
            return null;
        }

        return kind switch
        {
            CatAiDefaults.RenewalProjectWizardKind => "Creating renewal project",
            CatAiDefaults.UserGroupWizardKind => "Creating user group",
            _ => "Wizard in progress",
        };
    }

    private bool TryGetUserId(out int userId)
    {
        userId = 0;
        return int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId) && userId > 0;
    }

    private static string BuildTitle(string content)
    {
        var t = content.Trim().Replace('\r', ' ').Replace('\n', ' ');
        while (t.Contains("  ", StringComparison.Ordinal))
        {
            t = t.Replace("  ", " ", StringComparison.Ordinal);
        }

        if (t.Length > 60)
        {
            t = t[..57] + "...";
        }

        return string.IsNullOrWhiteSpace(t) ? "New chat" : t;
    }
}

public sealed class CatAiCreateConversationRequest
{
    public string? Title { get; set; }
}
