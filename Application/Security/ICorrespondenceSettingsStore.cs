using ClientManagementSystem.Models;

namespace ClientManagementSystem.Security;

public interface ICorrespondenceSettingsStore
{
    Task<CorrespondenceSettings> GetAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(CorrespondenceSettings settings, CancellationToken cancellationToken = default);
}
