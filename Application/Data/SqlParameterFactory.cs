using System.Data;
using Microsoft.Data.SqlClient;

namespace ClientManagementSystem.Data;

public static class SqlParameterFactory
{
    public static SqlParameter Int(string name, int? value) => new(name, SqlDbType.Int)
    {
        Value = value.HasValue ? value.Value : DBNull.Value
    };

    public static SqlParameter Bit(string name, bool value) => new(name, SqlDbType.Bit)
    {
        Value = value
    };

    public static SqlParameter Float(string name, double? value) => new(name, SqlDbType.Float)
    {
        Value = value.HasValue ? value.Value : DBNull.Value
    };

    public static SqlParameter DateTime2(string name, DateTime? value) => new(name, SqlDbType.DateTime2)
    {
        Value = value.HasValue ? value.Value : DBNull.Value
    };

    public static SqlParameter VarChar(string name, int size, string? value) => new(name, SqlDbType.VarChar, size)
    {
        Value = string.IsNullOrWhiteSpace(value) ? DBNull.Value : value
    };

    /// <summary>Like <see cref="VarChar"/> but keeps empty / whitespace so NOT NULL columns (e.g. Address1, City)
    /// can be written as <c>''</c> instead of NULL — required for UPDATEs that assign columns directly.</summary>
    public static SqlParameter VarCharAsWritten(string name, int size, string? value) => new(name, SqlDbType.VarChar, size)
    {
        Value = value is null ? DBNull.Value : value
    };

    public static SqlParameter NVarChar(string name, int size, string? value) => new(name, SqlDbType.NVarChar, size)
    {
        Value = string.IsNullOrWhiteSpace(value) ? DBNull.Value : value
    };

    public static SqlParameter Decimal(string name, decimal? value) => new(name, SqlDbType.Decimal)
    {
        Precision = 18,
        Scale = 2,
        Value = value.HasValue ? value.Value : DBNull.Value
    };

    public static SqlParameter BigInt(string name, long value) => new(name, SqlDbType.BigInt)
    {
        Value = value
    };

    public static SqlParameter Date(string name, DateTime? value) => new(name, SqlDbType.Date)
    {
        Value = value.HasValue ? value.Value.Date : DBNull.Value
    };

    public static SqlParameter OutputInt(string name) => new(name, SqlDbType.Int)
    {
        Direction = ParameterDirection.Output
    };
}
