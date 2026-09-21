using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartZoom.Core.Routing;

/// <summary>Reads and writes an <see cref="AdapterId"/> as a plain JSON string, so settings stay hand-editable.</summary>
internal sealed class AdapterIdJsonConverter : JsonConverter<AdapterId>
{
    /// <inheritdoc />
    public override AdapterId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
        if (string.IsNullOrWhiteSpace(value))
            throw new JsonException("An adapter id must be a non-empty string, e.g. \"Browser\" or \"None\".");

        return new AdapterId(value);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, AdapterId value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value.Value);
    }

    /// <inheritdoc />
    public override AdapterId ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new(reader.GetString() ?? throw new JsonException("An adapter id must be a non-empty string."));

    /// <inheritdoc />
    public override void WriteAsPropertyName(Utf8JsonWriter writer, AdapterId value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WritePropertyName(value.Value);
    }
}
