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
        return reader.IsDBNull(ordinal) ? 0 : Convert.ToInt32(reader.GetValue(ordinal), System.Globalization.CultureInfo.InvariantCulture);
    }

    public static int? GetNullableInt32(this SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal)
            ? null
            : Convert.ToInt32(reader.GetValue(ordinal), System.Globalization.CultureInfo.InvariantCulture);
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
                return reader.IsDBNull(i)
                    ? null
                    : Convert.ToInt32(reader.GetValue(i), System.Globalization.CultureInfo.InvariantCulture);
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
        if (reader.IsDBNull(ordinal))
        {
            return 0L;
        }

        var value = reader.GetValue(ordinal);
        return value switch
        {
            long l => l,
            int i => i,
            decimal d => (long)d,
            _ => Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture)
        };
    }
}
