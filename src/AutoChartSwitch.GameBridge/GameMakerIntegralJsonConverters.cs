using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoChartSwitch.GameBridge;

internal sealed class GameMakerDecimalJsonConverter : JsonConverter<decimal>
{
    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetDecimal(out var number)) return number;
        if (reader.TokenType == JsonTokenType.String) return decimal.TryParse(reader.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var text) ? text : 0m;
        if (reader.TokenType == JsonTokenType.Null) return 0m;
        if (reader.TokenType == JsonTokenType.Number) { reader.Skip(); return 0m; }
        throw new JsonException("Expected a finite decimal.");
    }

    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
}

internal sealed class GameMakerInt32JsonConverter : JsonConverter<int>
{
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
        {
            if (reader.TryGetInt32(out var integer)) return integer;
            if (reader.TryGetDecimal(out var value) && value == decimal.Truncate(value) &&
                value is >= int.MinValue and <= int.MaxValue)
                return decimal.ToInt32(value);
        }
        else if (reader.TokenType == JsonTokenType.String &&
                 decimal.TryParse(reader.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var text) &&
                 text == decimal.Truncate(text) && text is >= int.MinValue and <= int.MaxValue)
            return decimal.ToInt32(text);

        throw new JsonException("Expected an integral 32-bit number.");
    }

    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options) =>
        writer.WriteNumberValue(value);
}

internal sealed class GameMakerInt64JsonConverter : JsonConverter<long>
{
    public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
        {
            if (reader.TryGetInt64(out var integer)) return integer;
            if (reader.TryGetDecimal(out var value) && value == decimal.Truncate(value) &&
                value is >= long.MinValue and <= long.MaxValue)
                return decimal.ToInt64(value);
        }
        else if (reader.TokenType == JsonTokenType.String &&
                 decimal.TryParse(reader.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var text) &&
                 text == decimal.Truncate(text) && text is >= long.MinValue and <= long.MaxValue)
            return decimal.ToInt64(text);

        throw new JsonException("Expected an integral 64-bit number.");
    }

    public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options) =>
        writer.WriteNumberValue(value);
}
