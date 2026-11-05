using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace TrackedSecretConfiguration.Tests;

public class TrackedSecretConfigurationTests
{
    [Fact]
    public void Tracked_appsettings_leave_secret_fields_empty()
    {
        var root = FindRepoRoot();
        var files = Directory.GetFiles(Path.Combine(root, "Application"), "appsettings*.json", SearchOption.TopDirectoryOnly);
        Assert.NotEmpty(files);

        foreach (var file in files)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            AssertSecretEmpty(doc.RootElement, "OpenAI", "ApiKey", Path.GetFileName(file));
            AssertSecretEmpty(doc.RootElement, "AzureBlob", "SasToken", Path.GetFileName(file));
        }
    }

    [Fact]
    public void Environment_variables_override_empty_tracked_secrets()
    {
        var root = FindRepoRoot();
        const string apiEnv = "OpenAI__ApiKey";
        const string sasEnv = "AzureBlob__SasToken";
        var previousApi = Environment.GetEnvironmentVariable(apiEnv);
        var previousSas = Environment.GetEnvironmentVariable(sasEnv);
        try
        {
            Environment.SetEnvironmentVariable(apiEnv, "unit-test-api-key");
            Environment.SetEnvironmentVariable(sasEnv, "unit-test-sas");

            var config = new ConfigurationBuilder()
                .AddJsonFile(Path.Combine(root, "Application", "appsettings.json"), optional: false)
                .AddJsonFile(Path.Combine(root, "Application", "appsettings.Development.json"), optional: false)
                .AddEnvironmentVariables()
                .Build();

            Assert.True(
                string.Equals(config["OpenAI:ApiKey"], "unit-test-api-key", StringComparison.Ordinal),
                "OpenAI:ApiKey did not come from OpenAI__ApiKey.");
            Assert.True(
                string.Equals(config["AzureBlob:SasToken"], "unit-test-sas", StringComparison.Ordinal),
                "AzureBlob:SasToken did not come from AzureBlob__SasToken.");
            Assert.False(string.IsNullOrWhiteSpace(config["OpenAI:BaseUrl"]), "OpenAI:BaseUrl missing.");
            Assert.False(string.IsNullOrWhiteSpace(config["AzureBlob:ContainerBaseUrl"]), "AzureBlob:ContainerBaseUrl missing.");
            Assert.Equal("gpt-4o", config["OpenAI:Deployment"]);
            Assert.Equal("gpt-4o", config["OpenAI:Model"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(apiEnv, previousApi);
            Environment.SetEnvironmentVariable(sasEnv, previousSas);
        }
    }

    [Fact]
    public void Migration_script_reads_sas_from_the_app_environment_variable()
    {
        var path = Path.Combine(FindRepoRoot(), "Application", "SqlScripts", "Migrate_ProjectFile_BlobPaths.ps1");
        var text = File.ReadAllText(path);
        Assert.Contains("$env:AzureBlob__SasToken", text, StringComparison.Ordinal);
        Assert.False(
            text.Contains("sig=", StringComparison.OrdinalIgnoreCase),
            "Migrate_ProjectFile_BlobPaths.ps1 contains a SAS signature parameter.");
    }

    [Fact]
    public void Web_project_enables_development_user_secrets()
    {
        var csproj = File.ReadAllText(Path.Combine(FindRepoRoot(), "Application", "ClientManagementSystem.csproj"));
        Assert.Contains("<UserSecretsId>9de316c2-610a-410e-abd4-6c50d333d11c</UserSecretsId>", csproj, StringComparison.Ordinal);
    }

    private static void AssertSecretEmpty(JsonElement root, string section, string name, string fileName)
    {
        var value = root.GetProperty(section).GetProperty(name).GetString();
        Assert.True(
            string.IsNullOrWhiteSpace(value),
            $"{fileName} {section}:{name} must be empty.");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ClientManagementSystem.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Repository root not found.");
    }
}
