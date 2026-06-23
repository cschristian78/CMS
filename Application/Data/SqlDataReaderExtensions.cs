using Microsoft.Data.SqlClient;

namespace ClientManagementSystem.Data;

public static class SqlDataReaderExtensions
{
    public static string? GetNullableString(this SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    /// <summary>Returns null when the result set has no column with this name.</summary>
    public static string? TryGetNullableString(this SqlDataReader reader, string columnName)
    {
        for (var i = 0; i < reader.FieldCount; i++)
        {
            if (string.Equals(reader.GetName(i), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return reader.IsDBNull(i) ? null : reader.GetString(i);
            }
        }

        return null;
    }

    public static int GetInt32OrDefault(this SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? 0 : reader.GetInt32(ordinal);
    }

    public static int? GetNullableInt32(this SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }

    public static double? GetNullableDouble(this SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : Convert.ToDouble(reader.GetValue(ordinal));
    }

    public static DateTime? GetNullableDateTime(this SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetDateTime(ordinal);
    }

    /// <summary>Returns null when the result set has no column with this name.</summary>
    public static int? TryGetNullableInt32(this SqlDataReader reader, string columnName)
    {
        for (var i = 0; i < reader.FieldCount; i++)
        {
            if (string.Equals(reader.GetName(i), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return reader.IsDBNull(i) ? null : reader.GetInt32(i);
            }
        }

        return null;
    }

    /// <summary>Returns null when the result set has no column with this name.</summary>
    public static DateTime? TryGetNullableDateTime(this SqlDataReader reader, string columnName)
    {
        for (var i = 0; i < reader.FieldCount; i++)
        {
            if (string.Equals(reader.GetName(i), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return reader.IsDBNull(i) ? null : reader.GetDateTime(i);
            }
        }

        return null;
    }

    public static DateTime GetDateTimeOrDefault(this SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? DateTime.MinValue : reader.GetDateTime(ordinal);
    }

    public static bool GetBooleanOrDefault(this SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return !reader.IsDBNull(ordinal) && reader.GetBoolean(ordinal);
    }

    /// <summary>Returns null when the result set has no column with this name.</summary>
    public static bool? TryGetNullableBoolean(this SqlDataReader reader, string columnName)
    {
        for (var i = 0; i < reader.FieldCount; i++)
        {
            if (string.Equals(reader.GetName(i), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return reader.IsDBNull(i) ? null : reader.GetBoolean(i);
            }
        }

        return null;
    }

    public static decimal? GetNullableDecimal(this SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetDecimal(ordinal);
    }

    /// <summary>Returns null when the result set has no column with this name.</summary>
    public static decimal? TryGetNullableDecimal(this SqlDataReader reader, string columnName)
    {
        for (var i = 0; i < reader.FieldCount; i++)
        {
            if (string.Equals(reader.GetName(i), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return reader.IsDBNull(i) ? null : reader.GetDecimal(i);
            }
        }

        return null;
    }

    public static long GetInt64OrDefault(this SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? 0L : reader.GetInt64(ordinal);
    }
}
