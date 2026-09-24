using ClientManagementSystem.Models;

namespace ClientManagementSystem.Security;

public interface ICatAiSettingsStore
{
    Task<CatAiSettings?> GetOverrideAsync(CancellationToken cancellationToken = default);

    Task SaveOverrideAsync(CatAiSettings settings, bool updateApiKey, CancellationToken cancellationToken = default);

    Task ClearOverrideAsync(CancellationToken cancellationToken = default);
}
