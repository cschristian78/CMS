using ClientManagementSystem.Models;

namespace ClientManagementSystem.Security;

public interface ISecureConnectionSettingsStore
{
    IReadOnlyList<string> AllowedServers { get; }
    Task<ServerConnectionSettings> GetAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(ServerConnectionSettings settings, CancellationToken cancellationToken = default);
    Task<ConnectionTestResult> TestConnectionAsync(ServerConnectionSettings settings, CancellationToken cancellationToken = default);
    Task<string> BuildConnectionStringAsync(CancellationToken cancellationToken = default);
    string BuildConnectionString(ServerConnectionSettings settings);
}
