using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharedKernel.Persistence.Abstractions.Repositories;

/// <summary>
/// Reads and writes an <see cref="EntityVersion"/> as its token text: a JSON string, or <c>null</c> for
/// <see cref="EntityVersion.None"/>.
/// </summary>
/// <remarks>
/// Applied to <see cref="EntityVersion"/> by attribute, so a DTO property of this type needs no registration — with
/// reflection-based serialization and with a source-generated <c>JsonSerializerContext</c> alike. A string that is not
/// a version fails deserialization with <see cref="JsonException"/>, as a malformed <c>If-Match</c> would.
/// </remarks>
public sealed class EntityVersionJsonConverter : JsonConverter<EntityVersion>
{
    /// <inheritdoc />
    public override EntityVersion Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return EntityVersion.None;

        // Before P-562 X4 the type had no JSON form and always serialized as an empty object; a document stored then
        // (a replayed idempotent response, say) reads as None rather than failing. Any other object is not a version.
        if (reader.TokenType == JsonTokenType.StartObject && reader.Read() && reader.TokenType == JsonTokenType.EndObject)
            return EntityVersion.None;

        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException("An entity version is a JSON string.");

        return EntityVersion.TryParse(reader.GetString(), out var version)
            ? version
            : throw new JsonException("The value is not an entity version.");
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, EntityVersion value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        if (value == EntityVersion.None)
            writer.WriteNullValue();
        else
            writer.WriteStringValue(value.ToString());
    }
}
