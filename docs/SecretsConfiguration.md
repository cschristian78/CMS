# OpenAI and Azure Blob credentials

`Application/appsettings.json` and `Application/appsettings.Development.json` keep the existing setting names and leave the secret values empty. The host is `WebApplication.CreateBuilder`, which loads configuration in this order:

1. `appsettings.json`
2. `appsettings.{Environment}.json` (`Development` for the local launch profile; `Production` when `ASPNETCORE_ENVIRONMENT` is unset, which is the usual IIS case)
3. User secrets, only when the environment is Development. The project `UserSecretsId` is `9de316c2-610a-410e-abd4-6c50d333d11c`.
4. Environment variables
5. Command-line arguments

Later sources replace earlier ones for the same key. An empty string in the tracked JSON is therefore replaced by a user secret or environment variable. Do not pass these secrets on the command line; they are stored in shell history and process listings.

Non-secret keys stay in the tracked JSON files. That includes `OpenAI:Model`, `OpenAI:BaseUrl`, `OpenAI:Deployment`, `OpenAI:ApiVersion`, `OpenAI:Temperature`, `OpenAI:MaxTokens`, `OpenAI:MaxToolRounds`, `OpenAI:SystemPrompt`, `AzureBlob:ContainerBaseUrl`, and `AzureBlob:MaxUploadBytes`.

`docs/appsettings.Secrets.example.json` shows only the secret keys. It is not named `appsettings.json` or `appsettings.{Environment}.json`, so the host does not load it, and it is not part of the web project output.

## Exact setting names

| Purpose | Configuration key | Environment variable | User-secret name |
| --- | --- | --- | --- |
| CatAI API key | `OpenAI:ApiKey` | `OpenAI__ApiKey` | `OpenAI:ApiKey` |
| Azure Blob container SAS | `AzureBlob:SasToken` | `AzureBlob__SasToken` | `AzureBlob:SasToken` |

The double underscore is the environment-variable form of the configuration key. `AzureBlob:SasToken` may include or omit a leading `?`. `AzureProjectBlobStorage` trims one leading `?`.

The one-time script `Application/SqlScripts/Migrate_ProjectFile_BlobPaths.ps1` reads the same `AzureBlob__SasToken` variable. It no longer contains a SAS.

## Local development

The launch profile sets `ASPNETCORE_ENVIRONMENT=Development`, so user secrets are loaded. From `Application/`:

```cmd
dotnet user-secrets set "OpenAI:ApiKey" "<api-key>"
dotnet user-secrets set "AzureBlob:SasToken" "<sas>"
```

User secrets are stored outside the repository, under the user-secrets id above. Do not put the values back into `appsettings.json`, `appsettings.Development.json`, `launchSettings.json`, or the example file.

Setting `OpenAI__ApiKey` and `AzureBlob__SasToken` in the process environment also works and overrides user secrets. Use that when the app is not running as Development.

CatAI can also store an API key through the admin settings screen. That writes Data Protection ciphertext to `App_Data/cataisettings.protected.json`, which is gitignored. `CatAiSettingsResolver` uses that override when it is non-blank, then the configuration value. There is no admin store for the blob SAS. Blob storage reads only `AzureBlob:ContainerBaseUrl` and `AzureBlob:SasToken`.

## IIS deployment

Publish profiles already in the project:

- `Application/Properties/PublishProfiles/IISProfile.pubxml` and `CMS-Dev.pubxml`: MSDeploy to site `CMS-Dev` on `vm-usc-dev-03`. `CMS-Dev.pubxml` records the site path `C:\inetpub\wwwroot\CMS-Dev`.
- `Application/Properties/PublishProfiles/FolderProfile.pubxml`: folder publish to `E:\Sites\CMS-Dev1`.

`Application/web.config` stays free of secret values. It hosts the app in-process with AspNetCoreModuleV2 (`processPath="dotnet"`, `arguments=".\ClientManagementSystem.dll"`). Publish copies `appsettings.json` with empty secret values and replaces `web.config`. `SkipExtraFilesOnServer` is true, so files that are not in the package remain, but `appsettings.json` and `web.config` are in the package and are replaced. A secret edited only into the deployed `appsettings.json` or `web.config` will be lost on the next publish.

Set both environment variables on the server before publishing this change. In IIS Manager, select the site (`CMS-Dev`, and the site that uses `E:\Sites\CMS-Dev1` if that folder is served by IIS):

1. Open Configuration Editor.
2. Section: `system.webServer/aspNetCore`.
3. Open `environmentVariables` and add `OpenAI__ApiKey` and `AzureBlob__SasToken`.
4. Write the change to **ApplicationHost.config** for that site's location path, not to the site Web.config.
5. Recycle the application pool. The module reads environment variables when the process starts.

The same names can be set as machine or app-pool-account environment variables. Recycle the pool after either change.

This is the shape only. Do not commit it, and do not fill the values into the repository `web.config`:

```xml
<aspNetCore processPath="dotnet" arguments=".\ClientManagementSystem.dll" stdoutLogEnabled="false" stdoutLogFile=".\logs\stdout" hostingModel="inprocess">
  <environmentVariables>
    <environmentVariable name="OpenAI__ApiKey" value="" />
    <environmentVariable name="AzureBlob__SasToken" value="" />
  </environmentVariables>
</aspNetCore>
```

A server-only `appsettings.Production.json` is also loaded when the environment is Production, and it is not in source control today. Prefer the environment variables above so a later publish cannot overwrite them. If a Production file is used, do not commit it with real values.

## What fails closed

Startup does not construct blob storage. The first blob operation constructs `AzureProjectBlobStorage`. If `AzureBlob:ContainerBaseUrl` or `AzureBlob:SasToken` is blank, that constructor throws `AzureBlob:ContainerBaseUrl and AzureBlob:SasToken must be configured.`

CatAI chat throws `CatAI is not configured. Ask an administrator to set OpenAI:ApiKey on the server.` when the resolved API key is blank. An existing admin override file on the server still supplies the key after `appsettings.json` is emptied.

## How to validate

Do not call OpenAI, Azure OpenAI, or Azure Blob with the removed credentials, and do not use the in-app CatAI connection test to see whether the old key still works. Do not run `dotnet user-secrets list` in a captured log; it prints values.

Tracked files, from the repository root:

```cmd
python scripts/check-tracked-secrets.py
python scripts/check-tracked-secrets.py --self-test
dotnet build ClientManagementSystem.sln
dotnet test ClientManagementSystem.sln
```

The secret check prints paths and rule names only. It exits 0 when `OpenAI:ApiKey` and `AzureBlob:SasToken` are empty in every tracked `appsettings*.json`, and when no tracked project file outside `node_modules` contains a SAS signature or a literal `$sas = '...'` assignment. The test project checks the same empty fields, checks that `OpenAI__ApiKey` and `AzureBlob__SasToken` replace those empty values, and checks that the migration script names `AzureBlob__SasToken`. Test failure text does not include setting values.

Local presence, without printing values (PowerShell):

```powershell
$envNames = 'OpenAI__ApiKey','AzureBlob__SasToken'
foreach ($name in $envNames) {
  $set = -not [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name))
  Write-Output "$name $(if ($set) { 'set' } else { 'missing' })"
}
$id = '9de316c2-610a-410e-abd4-6c50d333d11c'
$secretsPath = Join-Path $env:APPDATA "Microsoft\UserSecrets\$id\secrets.json"
if (Test-Path $secretsPath) {
  $doc = Get-Content $secretsPath -Raw | ConvertFrom-Json
  foreach ($key in 'OpenAI:ApiKey','AzureBlob:SasToken') {
    $set = -not [string]::IsNullOrWhiteSpace([string]$doc.$key)
    Write-Output "$key $(if ($set) { 'set' } else { 'missing' })"
  }
} else {
  Write-Output "user-secrets file missing"
}
```

On Linux and macOS the user-secrets file is `~/.microsoft/usersecrets/9de316c2-610a-410e-abd4-6c50d333d11c/secrets.json`.

After the app is running with the new values, an administrator can open CatAI settings. The API returns `ApiKeyConfigured` and a masked key. That check does not show the raw key and does not call the provider. Confirm blob configuration by checking that `AzureBlob__SasToken` is non-empty in the site environment. Do not send the SAS to Azure as a liveness test of the removed token.

## Rotation and revocation

Deleting the values from the current files does not remove them from git history, from clones or forks, or from servers that already copied the old files. Treat both credentials as exposed. Rotate them at the issuing provider before relying on this change. This repository change does not rotate anything and does not rewrite history.

### OpenAI credential

The tracked `OpenAI:BaseUrl` host ends in `.openai.azure.com` and has no extra path. `OpenAI:Deployment` is `gpt-4o`. `CatAiChatService` treats a base URL that contains `.openai.azure.com` as Azure OpenAI and sends the key in the `api-key` header. The removed value is a 32-character hexadecimal string, which is an Azure OpenAI key, not an OpenAI platform key (those use an `sk-` prefix).

Revoke it on the Azure OpenAI resource for that host: Azure portal, Keys and Endpoint, Regenerate the key that was committed. If it is not clear which of the two keys was committed, regenerate both and update the running configuration in the same step. Then set the new value in `OpenAI__ApiKey` or in the Development user secret. If `App_Data/cataisettings.protected.json` on a server still holds the old key, update or clear that admin override, because the override wins over configuration.

Revoking a key at platform.openai.com does not invalidate this Azure OpenAI key.

### Azure Blob SAS

The same SAS was stored in both appsettings files and in `Migrate_ProjectFile_BlobPaths.ps1`. It is a service SAS. The parameters present are `sv`, `spr`, `st`, `se`, `sr`, `sp`, and `sig`. `sv` is `2020-02-10`, `spr` is `https`, `sr` is `c` (container), and `sp` is `racwlm` (read, add, create, write, list, move). The start year is 2026 and the expiry year is 2045. There is no stored-access-policy identifier (`si`) and there are no user-delegation fields (`skoid`, `sktid`, `skt`, `ske`, `sks`, `skv`).

Azure cannot revoke that SAS on its own. Invalidate it by rotating the storage account access key that signed it (Storage account, Security + networking, Access keys, Rotate). If you do not know which key signed it, rotate both key1 and key2. Rotation invalidates every SAS and every client that uses those account keys, not only this token. Then create a replacement SAS outside the repository, with a short expiry, and store it only in `AzureBlob__SasToken` (and in the Development user secret if local blob access is required). The container URL in `AzureBlob:ContainerBaseUrl` is not a secret and stays in the tracked files.

### Git history

A history purge (git filter-repo, BFG, or an equivalent) needs separately approved coordination. It requires a force-push and follow-up on clones, forks, CI caches, and packages. It does not make the already exposed credentials safe. Rotate the Azure OpenAI key and invalidate the SAS first. Do not rewrite history as part of removing the values from the current files.
