using Microsoft.Data.SqlClient;

namespace ClientManagementSystem.Data;

public interface IDbExecutor
{
    Task<IReadOnlyList<T>> QueryTextAsync<T>(string sql, Func<SqlDataReader, T> map, IEnumerable<SqlParameter>? parameters = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<T>> QueryAsync<T>(string storedProcedure, Func<SqlDataReader, T> map, IEnumerable<SqlParameter>? parameters = null, CancellationToken cancellationToken = default);
    Task<T?> QuerySingleOrDefaultAsync<T>(string storedProcedure, Func<SqlDataReader, T> map, IEnumerable<SqlParameter>? parameters = null, CancellationToken cancellationToken = default);
    Task<int> ExecuteScalarIntAsync(string storedProcedure, IEnumerable<SqlParameter>? parameters = null, CancellationToken cancellationToken = default);
    Task<int> ExecuteWithOutputIntAsync(string storedProcedure, IEnumerable<SqlParameter> parameters, string outputParameterName, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, int?>> ExecuteWithOutputIntsAsync(string storedProcedure, IEnumerable<SqlParameter> parameters, CancellationToken cancellationToken = default);
    Task ExecuteAsync(string storedProcedure, IEnumerable<SqlParameter>? parameters = null, CancellationToken cancellationToken = default);
    Task ExecuteTextAsync(string sql, IEnumerable<SqlParameter>? parameters = null, CancellationToken cancellationToken = default);

    Task<RawSqlTableResult> QueryTextAsTableAsync(string sql, IEnumerable<SqlParameter>? parameters = null, CancellationToken cancellationToken = default);
}
