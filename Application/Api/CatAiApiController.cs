using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace ClientManagementSystem.Api;

[Route("api/catai")]
[ApiController]
public sealed class CatAiApiController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<CatAiApiController> _logger;

    public CatAiApiController(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger<CatAiApiController> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _environment = environment;
        _logger = logger;
    }

    [HttpPost("chat")]
    public async Task<ActionResult<CatAiChatResponseBody>> ChatAsync(
        [FromBody] CatAiChatRequestBody body,
        CancellationToken cancellationToken)
    {
        if (body.Messages is null || body.Messages.Count == 0)
        {
            return BadRequest(new ProblemDetails { Title = "Invalid request", Detail = "Provide at least one message." });
        }

        var apiKey = _configuration["OpenAI:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("CatAI request failed because OpenAI:ApiKey is missing.");
            return Problem(
                detail: "CatAI is not configured. Ask an administrator to set OpenAI:ApiKey on the server.",
                title: "Configuration missing",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var model = _configuration["OpenAI:Model"];
        if (string.IsNullOrWhiteSpace(model))
        {
            model = "gpt-4o-mini";
        }

        var baseUrl = _configuration["OpenAI:BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            baseUrl = "https://api.openai.com/v1";
        }
        var isAzureOpenAi = baseUrl.Contains(".openai.azure.com", StringComparison.OrdinalIgnoreCase);
        var apiVersion = _configuration["OpenAI:ApiVersion"];
        if (string.IsNullOrWhiteSpace(apiVersion))
        {
            apiVersion = "2024-06-01";
        }
        var deployment = _configuration["OpenAI:Deployment"];
        var endpoint = isAzureOpenAi
            ? BuildAzureEndpoint(baseUrl, deployment, model, apiVersion)
            : $"{baseUrl.TrimEnd('/')}/chat/completions";
        object outgoing;
        if (isAzureOpenAi)
        {
            outgoing = new
            {
                messages = body.Messages.Select(m => new { role = m.Role, content = m.Content }),
            };
        }
        else
        {
            outgoing = new
            {
                model,
                messages = body.Messages.Select(m => new { role = m.Role, content = m.Content }),
            };
        }

        var json = JsonSerializer.Serialize(outgoing);
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(json, Encoding.UTF8)
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        if (isAzureOpenAi)
        {
            request.Headers.Add("api-key", apiKey);
        }
        else
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        try
        {
            using var client = _httpClientFactory.CreateClient();
            using var response = await client.SendAsync(request, cancellationToken);
            var raw = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "CatAI OpenAI request failed with status {StatusCode}. Response: {Body}",
                    (int)response.StatusCode,
                    raw);
                var detail = BuildProviderErrorDetail((int)response.StatusCode, raw, isAzureOpenAi, _environment.IsDevelopment());
                return Problem(
                    detail: detail,
                    title: "CatAI unavailable",
                    statusCode: StatusCodes.Status502BadGateway);
            }

            var openAiResponse = JsonSerializer.Deserialize<OpenAiChatResponse>(raw);
            var answer = openAiResponse?.Choices?.FirstOrDefault()?.Message?.Content?.Trim();
            if (string.IsNullOrWhiteSpace(answer))
            {
                return Problem(
                    detail: "The AI provider returned an empty response.",
                    title: "CatAI unavailable",
                    statusCode: StatusCodes.Status502BadGateway);
            }

            return Ok(new CatAiChatResponseBody { Reply = answer, Model = model });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CatAI backend chat request failed.");
            return Problem(
                detail: _environment.IsDevelopment()
                    ? ex.Message
                    : "The AI request could not be completed. Please try again.",
                title: "CatAI unavailable",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    public sealed class CatAiChatRequestBody
    {
        public List<CatAiMessageBody> Messages { get; set; } = new();
    }

    public sealed class CatAiMessageBody
    {
        public string Role { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
    }

    public sealed class CatAiChatResponseBody
    {
        public string Reply { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
    }

    private sealed class OpenAiChatResponse
    {
        public List<OpenAiChoice>? Choices { get; set; }
    }

    private sealed class OpenAiChoice
    {
        public OpenAiMessage? Message { get; set; }
    }

    private sealed class OpenAiMessage
    {
        public string? Content { get; set; }
    }

    private static string BuildAzureEndpoint(string baseUrl, string? deployment, string model, string apiVersion)
    {
        var deploymentName = string.IsNullOrWhiteSpace(deployment) ? model : deployment;
        return $"{baseUrl.TrimEnd('/')}/openai/deployments/{Uri.EscapeDataString(deploymentName)}/chat/completions?api-version={Uri.EscapeDataString(apiVersion)}";
    }

    private static string BuildProviderErrorDetail(int statusCode, string raw, bool isAzureOpenAi, bool isDevelopment)
    {
        var fallback = "The AI provider request failed. Please try again.";
        if (!isDevelopment)
        {
            return fallback;
        }

        if (statusCode == StatusCodes.Status401Unauthorized || statusCode == StatusCodes.Status403Forbidden)
        {
            return isAzureOpenAi
                ? $"Provider authentication failed (status {statusCode}). Verify OpenAI:ApiKey for Azure OpenAI. Provider response: {raw}"
                : $"Provider authentication failed (status {statusCode}). Verify OpenAI:ApiKey. Provider response: {raw}";
        }

        if (statusCode == StatusCodes.Status404NotFound)
        {
            return isAzureOpenAi
                ? $"Azure deployment was not found (status 404). Verify OpenAI:Deployment and OpenAI:ApiVersion. Provider response: {raw}"
                : $"Provider endpoint was not found (status 404). Verify OpenAI:BaseUrl. Provider response: {raw}";
        }

        return $"Provider request failed (status {statusCode}). Response: {raw}";
    }
}
