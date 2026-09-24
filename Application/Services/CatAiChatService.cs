using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClientManagementSystem.Models;

namespace ClientManagementSystem.Services;

public interface ICatAiChatService
{
    Task<CatAiChatResult> ChatAsync(
        IReadOnlyList<CatAiChatMessageDto> messages,
        string? wizardKind = null,
        CatAiActorContext? actor = null,
        CancellationToken cancellationToken = default);

    Task<ConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken = default);
}

public sealed class CatAiChatService : ICatAiChatService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ICatAiSettingsResolver _settingsResolver;
    private readonly ICatAiToolService _tools;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<CatAiChatService> _logger;

    public CatAiChatService(
        IHttpClientFactory httpClientFactory,
        ICatAiSettingsResolver settingsResolver,
        ICatAiToolService tools,
        IHostEnvironment environment,
        ILogger<CatAiChatService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settingsResolver = settingsResolver;
        _tools = tools;
        _environment = environment;
        _logger = logger;
    }

    public async Task<CatAiChatResult> ChatAsync(
        IReadOnlyList<CatAiChatMessageDto> messages,
        string? wizardKind = null,
        CatAiActorContext? actor = null,
        CancellationToken cancellationToken = default)
    {
        var settings = await _settingsResolver.ResolveAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            throw new InvalidOperationException(
                "CatAI is not configured. Ask an administrator to set OpenAI:ApiKey on the server.");
        }

        actor ??= new CatAiActorContext { UserId = 0, UserType = "Basic" };

        var toolsUsed = new List<string>();
        var downloadLinks = new List<(string Label, string Url)>();
        var systemPrompt = settings.SystemPrompt;
        systemPrompt = systemPrompt + "\n\n" + CatAiToolPermissions.PermissionSystemAddendum(actor);
        if (!string.IsNullOrWhiteSpace(wizardKind))
        {
            if (!CatAiToolPermissions.CanStartCreateWizard(actor))
            {
                wizardKind = null;
            }
            else
            {
                systemPrompt = systemPrompt + "\n\n" + CatAiDefaults.WizardSystemAddendum(wizardKind);
            }
        }

        var apiMessages = new List<Dictionary<string, object?>>
        {
            new()
            {
                ["role"] = "system",
                ["content"] = systemPrompt,
            }
        };

        foreach (var m in messages)
        {
            var role = (m.Role ?? string.Empty).Trim().ToLowerInvariant();
            if (role is not ("user" or "assistant" or "system"))
            {
                continue;
            }

            apiMessages.Add(new Dictionary<string, object?>
            {
                ["role"] = role,
                ["content"] = m.Content ?? string.Empty,
            });
        }

        var isAzure = settings.BaseUrl.Contains(".openai.azure.com", StringComparison.OrdinalIgnoreCase);
        var endpoint = isAzure
            ? BuildAzureEndpoint(settings)
            : $"{settings.BaseUrl.TrimEnd('/')}/chat/completions";

        var toolDefs = _tools.GetToolDefinitions(actor);
        string? finalReply = null;
        var maxRounds = Math.Clamp(settings.MaxToolRounds, 1, 10);

        for (var round = 0; round < maxRounds; round++)
        {
            var body = BuildRequestBody(settings, apiMessages, toolDefs, isAzure);
            var raw = await SendAsync(endpoint, settings.ApiKey, isAzure, body, cancellationToken);
            using var doc = JsonDocument.Parse(raw);
            var choice = doc.RootElement.GetProperty("choices")[0];
            var message = choice.GetProperty("message");

            var assistantContent = message.TryGetProperty("content", out var contentEl)
                && contentEl.ValueKind == JsonValueKind.String
                    ? contentEl.GetString()
                    : null;

            List<ToolCall> toolCalls = [];
            if (message.TryGetProperty("tool_calls", out var toolCallsEl)
                && toolCallsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var tc in toolCallsEl.EnumerateArray())
                {
                    var id = tc.GetProperty("id").GetString() ?? Guid.NewGuid().ToString("N");
                    var fn = tc.GetProperty("function");
                    var name = fn.GetProperty("name").GetString() ?? string.Empty;
                    var args = fn.TryGetProperty("arguments", out var a) ? a.GetString() ?? "{}" : "{}";
                    toolCalls.Add(new ToolCall(id, name, args));
                }
            }

            if (toolCalls.Count == 0)
            {
                finalReply = string.IsNullOrWhiteSpace(assistantContent)
                    ? "I could not produce a response."
                    : assistantContent.Trim();
                break;
            }

            // Append assistant message with tool_calls for the next round.
            var assistantMsg = new Dictionary<string, object?>
            {
                ["role"] = "assistant",
                ["content"] = assistantContent,
                ["tool_calls"] = toolCalls.Select(t => new
                {
                    id = t.Id,
                    type = "function",
                    function = new { name = t.Name, arguments = t.Arguments },
                }).ToList(),
            };
            apiMessages.Add(assistantMsg);

            foreach (var call in toolCalls)
            {
                if (!string.IsNullOrWhiteSpace(call.Name) && !toolsUsed.Contains(call.Name, StringComparer.OrdinalIgnoreCase))
                {
                    toolsUsed.Add(call.Name);
                }

                var resultJson = await _tools.ExecuteAsync(call.Name, call.Arguments, actor, cancellationToken);
                HarvestDownloadLinks(resultJson, downloadLinks);
                apiMessages.Add(new Dictionary<string, object?>
                {
                    ["role"] = "tool",
                    ["tool_call_id"] = call.Id,
                    ["content"] = resultJson,
                });
            }
        }

        finalReply ??= "I reached the tool-call limit before finishing. Please narrow the question and try again.";
        finalReply = EnsureDownloadLinksInReply(finalReply, downloadLinks);

        return new CatAiChatResult
        {
            Reply = finalReply,
            Model = isAzure
                ? (string.IsNullOrWhiteSpace(settings.Deployment) ? settings.Model : settings.Deployment)
                : settings.Model,
            ToolsUsed = toolsUsed,
        };
    }

    public async Task<ConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await ChatAsync(
                [new CatAiChatMessageDto { Role = "user", Content = "Reply with exactly: ok" }],
                wizardKind: null,
                actor: new CatAiActorContext { UserId = 0, UserType = "Administrator" },
                cancellationToken: cancellationToken);
            return ConnectionTestResult.Success($"CatAI responded via {result.Model}.");
        }
        catch (Exception ex)
        {
            return ConnectionTestResult.Failure(ex.Message);
        }
    }

    private object BuildRequestBody(
        CatAiSettings settings,
        List<Dictionary<string, object?>> messages,
        IReadOnlyList<object> tools,
        bool isAzure)
    {
        if (isAzure)
        {
            return new
            {
                messages,
                temperature = settings.Temperature,
                max_tokens = settings.MaxTokens,
                tools,
                tool_choice = "auto",
            };
        }

        return new
        {
            model = settings.Model,
            messages,
            temperature = settings.Temperature,
            max_tokens = settings.MaxTokens,
            tools,
            tool_choice = "auto",
        };
    }

    private async Task<string> SendAsync(
        string endpoint,
        string apiKey,
        bool isAzure,
        object body,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(body, JsonOptions);
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(json, Encoding.UTF8)
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        if (isAzure)
        {
            request.Headers.Add("api-key", apiKey);
        }
        else
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        using var client = _httpClientFactory.CreateClient();
        using var response = await client.SendAsync(request, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("CatAI provider failed {Status}: {Body}", (int)response.StatusCode, raw);
            var detail = _environment.IsDevelopment()
                ? $"Provider request failed ({(int)response.StatusCode}): {raw}"
                : "The AI provider request failed. Please try again.";
            throw new InvalidOperationException(detail);
        }

        return raw;
    }

    private static string BuildAzureEndpoint(CatAiSettings settings)
    {
        var deployment = string.IsNullOrWhiteSpace(settings.Deployment) ? settings.Model : settings.Deployment;
        return $"{settings.BaseUrl.TrimEnd('/')}/openai/deployments/{Uri.EscapeDataString(deployment)}/chat/completions?api-version={Uri.EscapeDataString(settings.ApiVersion)}";
    }

    private static void HarvestDownloadLinks(string resultJson, List<(string Label, string Url)> links)
    {
        if (string.IsNullOrWhiteSpace(resultJson) || links is null)
        {
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(resultJson);
            var root = doc.RootElement;
            if (!root.TryGetProperty("downloadUrl", out var urlEl)
                || urlEl.ValueKind != JsonValueKind.String)
            {
                return;
            }

            var url = urlEl.GetString()?.Trim();
            if (string.IsNullOrWhiteSpace(url) || !url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var label = ReadStringProp(root, "linkLabel");
            if (string.IsNullOrWhiteSpace(label))
            {
                label = ReadStringProp(root, "fileName");
            }

            if (string.IsNullOrWhiteSpace(label))
            {
                label = "ProjectReport.pdf";
            }

            if (links.Any(l => string.Equals(l.Url, url, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            links.Add((label!, url));
        }
        catch (JsonException)
        {
            // Tool payloads that are not JSON objects are ignored.
        }
    }

    private static string? ReadStringProp(JsonElement root, string name)
        => root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    private static string EnsureDownloadLinksInReply(
        string reply,
        IReadOnlyList<(string Label, string Url)> links)
    {
        if (links.Count == 0)
        {
            return reply;
        }

        var text = reply ?? string.Empty;
        foreach (var (label, url) in links)
        {
            var markdown = $"[{label}]({url})";
            if (text.Contains(markdown, StringComparison.Ordinal))
            {
                continue;
            }

            if (text.Contains(url, StringComparison.Ordinal))
            {
                // Prefer an aliased markdown link over a bare (often unreadable) SAS URL.
                text = text.Replace(url, markdown, StringComparison.Ordinal);
                continue;
            }

            text = text.TrimEnd() + "\n\n" + markdown;
        }

        return text;
    }

    private sealed record ToolCall(string Id, string Name, string Arguments);
}
