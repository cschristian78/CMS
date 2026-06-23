using Microsoft.Data.SqlClient;

namespace ClientManagementSystem.Data;

public interface ISqlConnectionFactory
{
    Task<SqlConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default);
}
