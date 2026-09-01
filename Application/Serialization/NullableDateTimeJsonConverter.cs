using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClientManagementSystem.Serialization;

/// <summary>
/// Treats JSON <c>""</c> as <see langword="null"/> when deserializing nullable dates.
/// Prevents model-binding failures when the SPA clears a date input.
/// </summary>
public sealed class NullableDateTimeJsonConverter : JsonConverter<DateTime?>
{
    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.Null => null,
            JsonTokenType.String => ParseString(reader.GetString()),
            _ => reader.TryGetDateTime(out var dt) ? dt : null,
        };
    }

    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(value.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    private static DateTime? ParseString(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
        {
            return dt;
        }

        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
        {
            return dt;
        }

        throw new JsonException($"The JSON value '{raw}' could not be converted to DateTime.");
    }
}
