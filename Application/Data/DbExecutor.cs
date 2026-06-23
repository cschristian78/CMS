using System.Data;
using Microsoft.Data.SqlClient;

namespace ClientManagementSystem.Data;

public sealed class DbExecutor : IDbExecutor
{
    private readonly ISqlConnectionFactory _connectionFactory;

    public DbExecutor(ISqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<T>> QueryTextAsync<T>(string sql, Func<SqlDataReader, T> map, IEnumerable<SqlParameter>? parameters = null, CancellationToken cancellationToken = default)
    {
        var results = new List<T>();
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var command = CreateTextCommand(connection, sql, parameters);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(map(reader));
        }

        return results;
    }

    public async Task<IReadOnlyList<T>> QueryAsync<T>(string storedProcedure, Func<SqlDataReader, T> map, IEnumerable<SqlParameter>? parameters = null, CancellationToken cancellationToken = default)
    {
        var results = new List<T>();
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, storedProcedure, parameters);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(map(reader));
        }

        return results;
    }

    public async Task<T?> QuerySingleOrDefaultAsync<T>(string storedProcedure, Func<SqlDataReader, T> map, IEnumerable<SqlParameter>? parameters = null, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, storedProcedure, parameters);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (await reader.ReadAsync(cancellationToken))
        {
            return map(reader);
        }

        return default;
    }

    public async Task<int> ExecuteScalarIntAsync(string storedProcedure, IEnumerable<SqlParameter>? parameters = null, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, storedProcedure, parameters);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null or DBNull ? 0 : Convert.ToInt32(result);
    }

    public async Task<int> ExecuteWithOutputIntAsync(
        string storedProcedure,
        IEnumerable<SqlParameter> parameters,
        string outputParameterName,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, storedProcedure, parameters);
        await command.ExecuteNonQueryAsync(cancellationToken);

        var output = command.Parameters[outputParameterName];
        return output?.Value is null or DBNull ? 0 : Convert.ToInt32(output.Value);
    }

    public async Task<IReadOnlyDictionary<string, int?>> ExecuteWithOutputIntsAsync(
        string storedProcedure,
        IEnumerable<SqlParameter> parameters,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, storedProcedure, parameters);
        await command.ExecuteNonQueryAsync(cancellationToken);

        var results = new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase);
        foreach (SqlParameter parameter in command.Parameters)
        {
            if (parameter.Direction is ParameterDirection.Output or ParameterDirection.InputOutput)
            {
                results[parameter.ParameterName] = parameter.Value is null or DBNull
                    ? null
                    : Convert.ToInt32(parameter.Value);
            }
        }

        return results;
    }

    public async Task ExecuteAsync(string storedProcedure, IEnumerable<SqlParameter>? parameters = null, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, storedProcedure, parameters);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task ExecuteTextAsync(string sql, IEnumerable<SqlParameter>? parameters = null, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var command = CreateTextCommand(connection, sql, parameters);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<RawSqlTableResult> QueryTextAsTableAsync(
        string sql,
        IEnumerable<SqlParameter>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var command = CreateTextCommand(connection, sql, parameters);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var fieldCount = reader.FieldCount;
        var columns = new string[fieldCount];
        for (var i = 0; i < fieldCount; i++)
        {
            columns[i] = reader.GetName(i);
        }

        var rows = new List<IReadOnlyList<object?>>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var values = new object?[fieldCount];
            for (var i = 0; i < fieldCount; i++)
            {
                values[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }

            rows.Add(values);
        }

        return new RawSqlTableResult
        {
            Columns = columns,
            Rows = rows
        };
    }

    private static SqlCommand CreateCommand(SqlConnection connection, string storedProcedure, IEnumerable<SqlParameter>? parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = storedProcedure;
        command.CommandType = CommandType.StoredProcedure;
        command.CommandTimeout = 60;

        if (parameters is not null)
        {
            foreach (var parameter in parameters)
            {
                command.Parameters.Add(parameter);
            }
        }

        return command;
    }

    private static SqlCommand CreateTextCommand(SqlConnection connection, string sql, IEnumerable<SqlParameter>? parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandType = CommandType.Text;
        command.CommandTimeout = 60;

        if (parameters is not null)
        {
            foreach (var parameter in parameters)
            {
                command.Parameters.Add(parameter);
            }
        }

        return command;
    }
}
