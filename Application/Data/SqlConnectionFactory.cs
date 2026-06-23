using ClientManagementSystem.Security;
using Microsoft.Data.SqlClient;

namespace ClientManagementSystem.Data;

public sealed class SqlConnectionFactory : ISqlConnectionFactory
{
    private readonly ISecureConnectionSettingsStore _settingsStore;

    public SqlConnectionFactory(ISecureConnectionSettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
    }

    public async Task<SqlConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connectionString = await _settingsStore.BuildConnectionStringAsync(cancellationToken);
        var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}
