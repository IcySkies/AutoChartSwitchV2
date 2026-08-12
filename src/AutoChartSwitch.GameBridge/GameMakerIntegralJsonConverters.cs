using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoChartSwitch.GameBridge;

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
                 int.TryParse(reader.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
        {
            return integer;
        }

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
                 long.TryParse(reader.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
        {
            return integer;
        }

        throw new JsonException("Expected an integral 64-bit number.");
    }

    public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options) =>
        writer.WriteNumberValue(value);
}
