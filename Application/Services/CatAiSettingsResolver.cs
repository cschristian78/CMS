using ClientManagementSystem.Models;
using ClientManagementSystem.Security;

namespace ClientManagementSystem.Services;

public interface ICatAiSettingsResolver
{
    Task<CatAiSettings> ResolveAsync(CancellationToken cancellationToken = default);
}

public sealed class CatAiSettingsResolver : ICatAiSettingsResolver
{
    private readonly IConfiguration _configuration;
    private readonly ICatAiSettingsStore _store;

    public CatAiSettingsResolver(IConfiguration configuration, ICatAiSettingsStore store)
    {
        _configuration = configuration;
        _store = store;
    }

    public async Task<CatAiSettings> ResolveAsync(CancellationToken cancellationToken = default)
    {
        var fromConfig = ReadFromConfiguration();
        var overrideSettings = await _store.GetOverrideAsync(cancellationToken);
        if (overrideSettings is null)
        {
            return fromConfig;
        }

        return new CatAiSettings
        {
            ApiKey = FirstNonBlank(overrideSettings.ApiKey, fromConfig.ApiKey),
            Model = FirstNonBlank(overrideSettings.Model, fromConfig.Model),
            BaseUrl = FirstNonBlank(overrideSettings.BaseUrl, fromConfig.BaseUrl),
            Deployment = FirstNonBlank(overrideSettings.Deployment, fromConfig.Deployment),
            ApiVersion = FirstNonBlank(overrideSettings.ApiVersion, fromConfig.ApiVersion),
            SystemPrompt = FirstNonBlank(overrideSettings.SystemPrompt, fromConfig.SystemPrompt),
            Temperature = overrideSettings.Temperature,
            MaxTokens = overrideSettings.MaxTokens > 0 ? overrideSettings.MaxTokens : fromConfig.MaxTokens,
            MaxToolRounds = overrideSettings.MaxToolRounds > 0 ? overrideSettings.MaxToolRounds : fromConfig.MaxToolRounds,
        };
    }

    private CatAiSettings ReadFromConfiguration()
    {
        var section = _configuration.GetSection("OpenAI");
        var temperature = 0.2;
        if (double.TryParse(section["Temperature"], out var t))
        {
            temperature = t;
        }

        var maxTokens = 1500;
        if (int.TryParse(section["MaxTokens"], out var mt) && mt > 0)
        {
            maxTokens = mt;
        }

        var maxRounds = 5;
        if (int.TryParse(section["MaxToolRounds"], out var mr) && mr > 0)
        {
            maxRounds = mr;
        }

        return new CatAiSettings
        {
            ApiKey = section["ApiKey"]?.Trim() ?? string.Empty,
            Model = FirstNonBlank(section["Model"], "gpt-4o"),
            BaseUrl = FirstNonBlank(section["BaseUrl"], "https://api.openai.com/v1"),
            Deployment = section["Deployment"]?.Trim() ?? string.Empty,
            ApiVersion = FirstNonBlank(section["ApiVersion"], "2024-06-01"),
            SystemPrompt = FirstNonBlank(section["SystemPrompt"], CatAiDefaults.SystemPrompt),
            Temperature = temperature,
            MaxTokens = maxTokens,
            MaxToolRounds = maxRounds,
        };
    }

    private static string FirstNonBlank(string? primary, string fallback)
    {
        var p = primary?.Trim();
        return string.IsNullOrEmpty(p) ? fallback : p;
    }
}
