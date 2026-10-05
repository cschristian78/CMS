using System.Text.Json;
using ClientManagementSystem.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;

namespace ClientManagementSystem.Security;

public sealed class SecureConnectionSettingsStore : ISecureConnectionSettingsStore
{
    private static readonly string[] ServerAllowList =
    [
        "vmus-sql-099",
        "VMUS-DPS-01",
        "MoodysDB",
        "VeriskCurrent",
        "VeriskPrevious"
    ];

    private readonly IDataProtector _protector;
    private readonly string _settingsFilePath;

    public SecureConnectionSettingsStore(IDataProtectionProvider protectionProvider, IWebHostEnvironment environment)
    {
        _protector = protectionProvider.CreateProtector("ClientManagementSystem.ConnectionSettings.v1");
        var appDataPath = Path.Combine(environment.ContentRootPath, "App_Data");
        Directory.CreateDirectory(appDataPath);
        _settingsFilePath = Path.Combine(appDataPath, "connectionsettings.protected.json");
    }

    public IReadOnlyList<string> AllowedServers => ServerAllowList;

    public async Task<ServerConnectionSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_settingsFilePath))
        {
            return GetDefaultSettings();
        }

        var json = await File.ReadAllTextAsync(_settingsFilePath, cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
        {
            return GetDefaultSettings();
        }

        var file = JsonSerializer.Deserialize<ProtectedConnectionSettingsFile>(json) ?? new ProtectedConnectionSettingsFile();
        var settings = new ServerConnectionSettings
        {
            Server = UnprotectOrDefault(file.Server, "vmus-sql-099"),
            Database = UnprotectOrDefault(file.Database, "BMS_CMS"),
            UserName = UnprotectOrDefault(file.UserName, null),
            Password = UnprotectOrDefault(file.Password, null),
            UseIntegratedSecurity = file.UseIntegratedSecurity
        };

        Validate(settings);
        return settings;
    }

    public async Task SaveAsync(ServerConnectionSettings settings, CancellationToken cancellationToken = default)
    {
        Validate(settings);

        var file = new ProtectedConnectionSettingsFile
        {
            Server = Protect(settings.Server),
            Database = Protect(settings.Database),
            UserName = Protect(settings.UserName),
            Password = Protect(settings.Password),
            UseIntegratedSecurity = settings.UseIntegratedSecurity || !settings.HasSqlCredentials
        };

        var json = JsonSerializer.Serialize(file, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_settingsFilePath, json, cancellationToken);
    }

    public async Task<ConnectionTestResult> TestConnectionAsync(ServerConnectionSettings settings, CancellationToken cancellationToken = default)
    {
        var usingIntegrated = settings.UseIntegratedSecurity || !settings.HasSqlCredentials;
        try
        {
            Validate(settings);
            var connectionString = BuildConnectionString(settings);
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            var authLabel = usingIntegrated
                ? "Windows Integrated Security (app process identity)"
                : $"SQL login '{settings.UserName}'";
            return ConnectionTestResult.Success($"Connected to {settings.Server}/{settings.Database} using {authLabel}.");
        }
        catch (Exception ex)
        {
            var hint = usingIntegrated
                ? " Windows Integrated Security uses the account that runs this web app (IIS app-pool identity or the user running Kestrel), not the browser signed-in user. Grant that account a SQL login on this server with access to the database, or uncheck Integrated Security and supply a SQL username/password."
                : " Verify the SQL username/password and that the login can open the selected database.";
            return ConnectionTestResult.Failure($"Connection failed: {ex.Message}.{hint}");
        }
    }

    public async Task<string> BuildConnectionStringAsync(CancellationToken cancellationToken = default)
    {
        var settings = await GetAsync(cancellationToken);
        return BuildConnectionString(settings);
    }

    public string BuildConnectionString(ServerConnectionSettings settings)
    {
        Validate(settings);

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = settings.Server,
            InitialCatalog = settings.Database,
            //Encrypt = true,
            TrustServerCertificate = true
            //MultipleActiveResultSets = false,
            //ConnectTimeout = 30
        };

        if (settings.UseIntegratedSecurity || !settings.HasSqlCredentials)
        {
            builder.IntegratedSecurity = true;
        }
        else
        {
            builder.IntegratedSecurity = false;
            builder.UserID = settings.UserName;
            builder.Password = settings.Password;
        }

        return builder.ConnectionString;
    }

    private static ServerConnectionSettings GetDefaultSettings() => new()
    {
        Server = "vmus-sql-099",
        Database = "BMS_CMS",
        UserName = null,
        Password = null,
        UseIntegratedSecurity = true
    };

    private static void Validate(ServerConnectionSettings settings)
    {
        // Server is a drop-down constrained to the curated allow-list; Database is a user-defined
        // free-text field, so we only enforce "non-empty" here and let SQL Server itself reject an
        // unknown database name during the Test connection step.
        if (!ServerAllowList.Contains(settings.Server, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The selected SQL Server is not approved for this application.");
        }

        if (string.IsNullOrWhiteSpace(settings.Database))
        {
            throw new InvalidOperationException("Database name is required.");
        }
    }

    private string? Protect(string? value) => string.IsNullOrWhiteSpace(value) ? null : _protector.Protect(value);

    private string? UnprotectOrDefault(string? protectedValue, string? defaultValue)
    {
        if (string.IsNullOrWhiteSpace(protectedValue))
        {
            return defaultValue;
        }

        return _protector.Unprotect(protectedValue);
    }

    private sealed class ProtectedConnectionSettingsFile
    {
        public string? Server { get; set; }
        public string? Database { get; set; }
        public string? UserName { get; set; }
        public string? Password { get; set; }
        public bool UseIntegratedSecurity { get; set; } = true;
    }
}
