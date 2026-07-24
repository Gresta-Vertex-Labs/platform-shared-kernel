using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using SharedKernel.AI.Abstractions.Models;

namespace SharedKernel.AI.Qdrant.Json;

/// <summary>
/// A <see cref="System.Text.Json"/> converter for <see cref="VectorValue"/>, used exclusively by this
/// package's own internal record-reconstruction round-trip (see <c>QdrantRecordMapper</c>).
/// </summary>
/// <remarks>
/// <see cref="VectorValue"/> ships no public settable properties (it is a closed, kind-checked scalar
/// union), so <see cref="System.Text.Json"/> cannot deserialize it without a converter. This is a plain
/// <see cref="System.Text.Json"/> extension point — not <c>Type.GetProperty</c>/<c>MakeGenericType</c>/
/// <c>Activator.CreateInstance</c> reflection, and not prohibited by this domain's no-reflection rule.
/// </remarks>
internal sealed class VectorValueJsonConverter : JsonConverter<VectorValue>
{
    private const string KindProperty = "k";
    private const string ValueProperty = "v";

    public override VectorValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        var kind = Enum.Parse<VectorValueKind>(root.GetProperty(KindProperty).GetString()!);
        var value = root.GetProperty(ValueProperty);

        return kind switch
        {
            VectorValueKind.String => VectorValue.From(value.GetString()!),
            VectorValueKind.Int64 => VectorValue.From(value.GetInt64()),
            VectorValueKind.Double => VectorValue.From(value.GetDouble()),
            VectorValueKind.Boolean => VectorValue.From(value.GetBoolean()),
            VectorValueKind.DateTimeOffset => VectorValue.From(
                DateTimeOffset.Parse(value.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)),
            _ => throw new JsonException($"Unknown VectorValueKind '{kind}'."),
        };
    }

    public override void Write(Utf8JsonWriter writer, VectorValue value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString(KindProperty, value.Kind.ToString());
        switch (value.Kind)
        {
            case VectorValueKind.String:
                writer.WriteString(ValueProperty, value.AsString);
                break;
            case VectorValueKind.Int64:
                writer.WriteNumber(ValueProperty, value.AsInt64);
                break;
            case VectorValueKind.Double:
                writer.WriteNumber(ValueProperty, value.AsDouble);
                break;
            case VectorValueKind.Boolean:
                writer.WriteBoolean(ValueProperty, value.AsBoolean);
                break;
            case VectorValueKind.DateTimeOffset:
                writer.WriteString(ValueProperty, value.AsDateTimeOffset.ToString("O", CultureInfo.InvariantCulture));
                break;
        }

        writer.WriteEndObject();
    }
}
