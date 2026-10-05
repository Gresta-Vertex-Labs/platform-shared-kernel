using System.Buffers;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using OpenFeature.Model;

namespace SharedKernel.FeatureManagement.Internal;

/// <summary>
/// Reads an OpenFeature <see cref="Value"/> into a typed object through source-generated
/// <see cref="JsonTypeInfo{T}"/> metadata. It writes JSON shaped by the target type, so a configuration string
/// becomes a JSON number, boolean or string according to the property it fills, then deserializes it.
/// </summary>
internal static class TypedValueConverter
{
    private const int MaxDepth = 64;

    public static bool TryConvert<T>(Value value, JsonTypeInfo<T> typeInfo, out T? result, out string? error)
    {
        try
        {
            var buffer = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                Write(writer, value, typeInfo, 0);
            }

            result = JsonSerializer.Deserialize(buffer.WrittenSpan, typeInfo);
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException or NotSupportedException or ArgumentException)
        {
            result = default;
            error = ex.Message;
            return false;
        }
    }

    private static void Write(Utf8JsonWriter writer, Value? value, JsonTypeInfo typeInfo, int depth)
    {
        if (depth > MaxDepth)
        {
            throw new JsonException("The value is nested too deeply.");
        }

        if (value is null || value.IsNull)
        {
            writer.WriteNullValue();
            return;
        }

        switch (typeInfo.Kind)
        {
            case JsonTypeInfoKind.Object when value.IsStructure:
                WriteObject(writer, value.AsStructure!, typeInfo, depth);
                return;

            case JsonTypeInfoKind.Object:
                throw new JsonException($"Expected an object for {typeInfo.Type.Name}.");

            case JsonTypeInfoKind.Enumerable:
                writer.WriteStartArray();
                JsonTypeInfo elementType = typeInfo.Options.GetTypeInfo(typeInfo.ElementType!);
                foreach (Value item in AsItems(value, typeInfo))
                {
                    Write(writer, item, elementType, depth + 1);
                }

                writer.WriteEndArray();
                return;

            case JsonTypeInfoKind.Dictionary when value.IsStructure:
                writer.WriteStartObject();
                JsonTypeInfo entryType = typeInfo.Options.GetTypeInfo(typeInfo.ElementType!);
                foreach (KeyValuePair<string, Value> entry in value.AsStructure!)
                {
                    writer.WritePropertyName(entry.Key);
                    Write(writer, entry.Value, entryType, depth + 1);
                }

                writer.WriteEndObject();
                return;

            case JsonTypeInfoKind.Dictionary:
                throw new JsonException($"Expected an object for {typeInfo.Type.Name}.");

            default:
                WriteScalar(writer, value, typeInfo.Type, depth);
                return;
        }
    }

    private static void WriteObject(Utf8JsonWriter writer, Structure structure, JsonTypeInfo typeInfo, int depth)
    {
        writer.WriteStartObject();
        foreach (JsonPropertyInfo property in typeInfo.Properties)
        {
            if (TryGetMember(structure, property.Name, out Value? member))
            {
                writer.WritePropertyName(property.Name);
                Write(writer, member, typeInfo.Options.GetTypeInfo(property.PropertyType), depth + 1);
            }
        }

        writer.WriteEndObject();
    }

    // Configuration keys are case-insensitive, so match JSON property names the same way.
    private static bool TryGetMember(Structure structure, string name, out Value? member)
    {
        if (structure.TryGetValue(name, out member))
        {
            return true;
        }

        foreach (KeyValuePair<string, Value> entry in structure)
        {
            if (string.Equals(entry.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                member = entry.Value;
                return true;
            }
        }

        member = null;
        return false;
    }

    private static IEnumerable<Value> AsItems(Value value, JsonTypeInfo typeInfo)
    {
        if (value.IsList)
        {
            return value.AsList!;
        }

        throw new JsonException($"Expected a list for {typeInfo.Type.Name}.");
    }

    private static void WriteScalar(Utf8JsonWriter writer, Value value, Type type, int depth)
    {
        Type target = Nullable.GetUnderlyingType(type) ?? type;

        if (target == typeof(bool))
        {
            writer.WriteBooleanValue(value switch
            {
                { IsBoolean: true } => value.AsBoolean!.Value,
                { IsString: true } when bool.TryParse(value.AsString, out bool parsed) => parsed,
                _ => throw new JsonException($"'{Describe(value)}' is not true or false."),
            });
            return;
        }

        if (IsNumber(target))
        {
            WriteNumber(writer, value);
            return;
        }

        if (target.IsEnum)
        {
            if (value.IsNumber || (value.IsString && long.TryParse(value.AsString, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)))
            {
                WriteNumber(writer, value);
            }
            else
            {
                writer.WriteStringValue(Describe(value));
            }

            return;
        }

        if (target == typeof(string))
        {
            writer.WriteStringValue(Describe(value));
            return;
        }

        WriteUntyped(writer, value, depth);
    }

    private static void WriteNumber(Utf8JsonWriter writer, Value value)
    {
        if (value.IsNumber)
        {
            writer.WriteNumberValue(value.AsDouble!.Value);
            return;
        }

        string text = value.AsString ?? throw new JsonException($"'{Describe(value)}' is not a number.");
        if (decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal exact))
        {
            writer.WriteNumberValue(exact);
        }
        else if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double approximate) && double.IsFinite(approximate))
        {
            writer.WriteNumberValue(approximate);
        }
        else
        {
            throw new JsonException($"'{text}' is not a number.");
        }
    }

    // Targets without a fixed JSON shape (object, JsonElement, custom converters): keep each value's own kind.
    private static void WriteUntyped(Utf8JsonWriter writer, Value value, int depth)
    {
        if (depth > MaxDepth)
        {
            throw new JsonException("The value is nested too deeply.");
        }

        switch (value)
        {
            case { IsNull: true }:
                writer.WriteNullValue();
                break;
            case { IsBoolean: true }:
                writer.WriteBooleanValue(value.AsBoolean!.Value);
                break;
            case { IsNumber: true }:
                writer.WriteNumberValue(value.AsDouble!.Value);
                break;
            case { IsStructure: true }:
                writer.WriteStartObject();
                foreach (KeyValuePair<string, Value> entry in value.AsStructure!)
                {
                    writer.WritePropertyName(entry.Key);
                    WriteUntyped(writer, entry.Value, depth + 1);
                }

                writer.WriteEndObject();
                break;
            case { IsList: true }:
                writer.WriteStartArray();
                foreach (Value item in value.AsList!)
                {
                    WriteUntyped(writer, item, depth + 1);
                }

                writer.WriteEndArray();
                break;
            default:
                writer.WriteStringValue(Describe(value));
                break;
        }
    }

    private static string Describe(Value value) => value switch
    {
        { IsString: true } => value.AsString!,
        { IsBoolean: true } => value.AsBoolean!.Value ? "true" : "false",
        { IsNumber: true } => value.AsDouble!.Value.ToString("R", CultureInfo.InvariantCulture),
        { IsDateTime: true } => value.AsDateTime!.Value.ToString("O", CultureInfo.InvariantCulture),
        _ => throw new JsonException("Expected a single value, not an object or a list."),
    };

    private static bool IsNumber(Type type) =>
        type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte)
        || type == typeof(uint) || type == typeof(ulong) || type == typeof(ushort) || type == typeof(sbyte)
        || type == typeof(double) || type == typeof(float) || type == typeof(decimal);
}
