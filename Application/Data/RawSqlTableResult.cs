namespace ClientManagementSystem.Data;

/// <summary>Result of a SELECT with dynamic columns (e.g. SELECT *).</summary>
public sealed class RawSqlTableResult
{
    public required IReadOnlyList<string> Columns { get; init; }
    public required IReadOnlyList<IReadOnlyList<object?>> Rows { get; init; }
}
