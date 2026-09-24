using System.Text.Json;
using ClientManagementSystem.Models;
using Microsoft.AspNetCore.DataProtection;

namespace ClientManagementSystem.Security;

public sealed class CatAiSettingsStore : ICatAiSettingsStore
{
    private readonly IDataProtector _protector;
    private readonly string _settingsFilePath;

    public CatAiSettingsStore(IDataProtectionProvider protectionProvider, IWebHostEnvironment environment)
    {
        _protector = protectionProvider.CreateProtector("ClientManagementSystem.CatAiSettings.v1");
        var appDataPath = Path.Combine(environment.ContentRootPath, "App_Data");
        Directory.CreateDirectory(appDataPath);
        _settingsFilePath = Path.Combine(appDataPath, "cataisettings.protected.json");
    }

    public async Task<CatAiSettings?> GetOverrideAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_settingsFilePath))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(_settingsFilePath, cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        var file = JsonSerializer.Deserialize<ProtectedCatAiSettingsFile>(json);
        if (file is null || !file.HasAnyValue)
        {
            return null;
        }

        return new CatAiSettings
        {
            ApiKey = Unprotect(file.ApiKey) ?? string.Empty,
            Model = Unprotect(file.Model) ?? string.Empty,
            BaseUrl = Unprotect(file.BaseUrl) ?? string.Empty,
            Deployment = Unprotect(file.Deployment) ?? string.Empty,
            ApiVersion = Unprotect(file.ApiVersion) ?? string.Empty,
            SystemPrompt = Unprotect(file.SystemPrompt) ?? string.Empty,
            Temperature = file.Temperature,
            MaxTokens = file.MaxTokens,
            MaxToolRounds = file.MaxToolRounds,
        };
    }

    public async Task SaveOverrideAsync(CatAiSettings settings, bool updateApiKey, CancellationToken cancellationToken = default)
    {
        var existing = await GetOverrideAsync(cancellationToken) ?? new CatAiSettings();
        var apiKey = updateApiKey ? settings.ApiKey : existing.ApiKey;

        var file = new ProtectedCatAiSettingsFile
        {
            ApiKey = Protect(apiKey),
            Model = Protect(NullIfBlank(settings.Model)),
            BaseUrl = Protect(NullIfBlank(settings.BaseUrl)),
            Deployment = Protect(NullIfBlank(settings.Deployment)),
            ApiVersion = Protect(NullIfBlank(settings.ApiVersion)),
            SystemPrompt = Protect(NullIfBlank(settings.SystemPrompt)),
            Temperature = settings.Temperature,
            MaxTokens = settings.MaxTokens,
            MaxToolRounds = settings.MaxToolRounds,
            HasAnyValue = true,
        };

        var json = JsonSerializer.Serialize(file, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_settingsFilePath, json, cancellationToken);
    }

    public Task ClearOverrideAsync(CancellationToken cancellationToken = default)
    {
        if (File.Exists(_settingsFilePath))
        {
            File.Delete(_settingsFilePath);
        }

        return Task.CompletedTask;
    }

    private string? Protect(string? value) => string.IsNullOrWhiteSpace(value) ? null : _protector.Protect(value);

    private string? Unprotect(string? protectedValue)
    {
        if (string.IsNullOrWhiteSpace(protectedValue))
        {
            return null;
        }

        try
        {
            return _protector.Unprotect(protectedValue);
        }
        catch
        {
            return null;
        }
    }

    private static string? NullIfBlank(string? value)
    {
        var t = value?.Trim();
        return string.IsNullOrEmpty(t) ? null : t;
    }

    private sealed class ProtectedCatAiSettingsFile
    {
        public bool HasAnyValue { get; set; }
        public string? ApiKey { get; set; }
        public string? Model { get; set; }
        public string? BaseUrl { get; set; }
        public string? Deployment { get; set; }
        public string? ApiVersion { get; set; }
        public string? SystemPrompt { get; set; }
        public double Temperature { get; set; } = 0.2;
        public int MaxTokens { get; set; } = 1500;
        public int MaxToolRounds { get; set; } = 5;
    }
}
