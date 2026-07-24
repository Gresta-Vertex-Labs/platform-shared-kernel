using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharedKernel.AI.Qdrant.Json;

/// <summary>
/// A <see cref="System.Text.Json"/> converter for <see cref="ReadOnlyMemory{T}"/> of <see cref="float"/>,
/// used exclusively by this package's own internal record-reconstruction round-trip.
/// </summary>
internal sealed class ReadOnlyMemoryFloatJsonConverter : JsonConverter<ReadOnlyMemory<float>>
{
    public override ReadOnlyMemory<float> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var array = JsonSerializer.Deserialize<float[]>(ref reader, options) ?? [];
        return array;
    }

    public override void Write(Utf8JsonWriter writer, ReadOnlyMemory<float> value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value.ToArray(), options);
}
