using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace SharedKernel.Primitives.Enums;

/// <summary>
/// Serializes a <see cref="SmartEnum{TEnum, TValue}"/> as its underlying <see cref="SmartEnum{TEnum, TValue}.Value"/>
/// and reads it back by looking that value up among the declared members.
/// </summary>
/// <typeparam name="TEnum">The concrete enumeration type.</typeparam>
/// <typeparam name="TValue">The type of the underlying value.</typeparam>
/// <remarks>
/// <para>
/// Without a converter a <see cref="SmartEnum{TEnum, TValue}"/> is write-only over JSON: the
/// default object serializer emits <c>{"Name":"Active","Value":1}</c> and then cannot read it back,
/// because every member is a singleton reached through a <c>private</c> constructor. This converter
/// makes the round trip work, and makes the wire form the value alone.
/// </para>
/// <para>
/// <b>The underlying value is the wire contract, not the name.</b> Renaming a member is then a
/// source-level change that leaves persisted and in-flight payloads readable, which is the same
/// trade-off a plain <c>enum</c> serialized numerically makes. Deserialization goes through
/// <see cref="SmartEnum{TEnum, TValue}.TryFromValue"/>, so an unknown value fails as a
/// <see cref="JsonException"/> naming the type and value rather than silently producing
/// <see langword="null"/> or a partly-initialised instance.
/// </para>
/// <para>
/// <b>Registration.</b> This package ships no <c>JsonSerializerContext</c> and adds no converter
/// to any global default — apply it where it is wanted, either per property or per options
/// instance:
/// <code>
/// // Per property:
/// public sealed record OrderDto(
///     [property: JsonConverter(typeof(SmartEnumJsonConverter&lt;OrderStatus, int&gt;))]
///     OrderStatus Status);
///
/// // Per options instance:
/// var options = new JsonSerializerOptions();
/// options.Converters.Add(new SmartEnumJsonConverter&lt;OrderStatus, int&gt;());
/// </code>
/// </para>
/// <para>
/// <b>AOT and trimming.</b> Verified clean: the package compiles with zero IL2026/IL3050 under
/// both <c>EnableTrimAnalyzer</c> and <c>EnableAotAnalyzer</c>. Getting there required care, so do
/// not "simplify" the value read and write back to the obvious calls. The reflection-based
/// <c>JsonSerializer.Deserialize&lt;T&gt;(ref reader, options)</c>,
/// <c>JsonSerializer.Serialize(writer, value, options)</c>, and
/// <c>JsonSerializerOptions.GetConverter(Type)</c> are all annotated
/// <c>[RequiresUnreferencedCode]</c> and <c>[RequiresDynamicCode]</c>, and each was measured
/// emitting IL2026 plus IL3050 from this file. Only the <c>JsonTypeInfo&lt;T&gt;</c> overloads,
/// resolved through <see cref="JsonSerializerOptions.GetTypeInfo(Type)"/>, are unannotated —
/// those are what this converter uses, which is also what lets a caller satisfy
/// <typeparamref name="TValue"/> from a source-generated <c>JsonSerializerContext</c>.
/// It is deliberately NOT a <see cref="JsonConverterFactory"/>: a factory would have to construct
/// closed generic converter types at runtime via <c>MakeGenericType</c>, exactly the reflection
/// this platform bars.
/// </para>
/// </remarks>
public sealed class SmartEnumJsonConverter<TEnum, TValue> : JsonConverter<TEnum?>
    where TEnum : SmartEnum<TEnum, TValue>
    where TValue : IEquatable<TValue>
{
    /// <summary>
    /// Reads a <typeparamref name="TEnum"/> from its underlying value.
    /// </summary>
    /// <param name="reader">The reader positioned at the value.</param>
    /// <param name="typeToConvert">The type being converted.</param>
    /// <param name="options">
    /// The serializer options, used to resolve the <c>JsonTypeInfo</c> for <typeparamref name="TValue"/> so
    /// the value is read exactly as it would be anywhere else in the payload.
    /// </param>
    /// <returns>
    /// The matching member, or <see langword="null"/> when the JSON value is <c>null</c>.
    /// </returns>
    /// <exception cref="JsonException">
    /// The value is present but matches no declared <typeparamref name="TEnum"/> member.
    /// </exception>
    public override TEnum? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        var value = JsonSerializer.Deserialize(ref reader, ValueTypeInfo(options));

        if (value is null)
        {
            return null;
        }

        if (!SmartEnum<TEnum, TValue>.TryFromValue(value, out var member))
        {
            throw new JsonException(
                $"'{value}' does not match any declared {typeof(TEnum).Name} member."
            );
        }

        return member;
    }

    /// <summary>
    /// Writes <paramref name="value"/> as its underlying <see cref="SmartEnum{TEnum, TValue}.Value"/>.
    /// </summary>
    /// <param name="writer">The writer to write to.</param>
    /// <param name="value">The member to write. <see langword="null"/> is written as a JSON null.</param>
    /// <param name="options">
    /// The serializer options, used to resolve the <c>JsonTypeInfo</c> for <typeparamref name="TValue"/>.
    /// </param>
    public override void Write(
        Utf8JsonWriter writer,
        TEnum? value,
        JsonSerializerOptions options
    )
    {
        ArgumentNullException.ThrowIfNull(writer);

        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        JsonSerializer.Serialize(writer, value.Value, ValueTypeInfo(options));
    }

    private static JsonTypeInfo<TValue> ValueTypeInfo(JsonSerializerOptions options) =>
        (JsonTypeInfo<TValue>)options.GetTypeInfo(typeof(TValue));
}
