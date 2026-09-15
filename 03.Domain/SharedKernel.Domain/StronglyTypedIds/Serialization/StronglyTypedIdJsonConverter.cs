using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharedKernel.Domain.StronglyTypedIds.Serialization;

/// <summary>
/// A System.Text.Json converter that reads and writes one strongly-typed identifier type as its bare underlying
/// value, including as a dictionary key.
/// </summary>
/// <typeparam name="TStronglyTypedId">The concrete identifier type, such as <c>OrderId</c>.</typeparam>
/// <typeparam name="TValue">The identifier's underlying key type, such as <see cref="Guid"/>.</typeparam>
/// <remarks>
/// <para>
/// <b>Usage.</b> <see cref="StronglyTypedIdJsonConverterFactory"/> creates this converter for every identifier
/// type; register the factory instead of this type. Apply this converter directly, with
/// <c>[JsonConverter(typeof(StronglyTypedIdJsonConverter&lt;OrderId, Guid&gt;))]</c>, only to cover a single
/// identifier type without the factory.
/// </para>
/// <para>
/// <b>Construction.</b> Each instance locates the identifier's public constructor taking one
/// <typeparamref name="TValue"/> and compiles a delegate for it. Creating the converter throws
/// <see cref="InvalidOperationException"/> when that constructor does not exist. The positional record
/// declaration <c>sealed record OrderId(Guid Value) : StronglyTypedId&lt;Guid&gt;(Value)</c> provides it.
/// </para>
/// <para>
/// <b>Values.</b> The underlying value is read and written with the converter the serializer options resolve
/// for <typeparamref name="TValue"/>, so its format follows those options. A JSON <c>null</c> reads as a
/// <see langword="null"/> identifier.
/// </para>
/// <para>
/// <b>Pitfall.</b> Dictionary keys use the <typeparamref name="TValue"/> converter's property-name support.
/// Every primitive, <see cref="Guid"/>, <see cref="string"/> and date type has it; a custom underlying type
/// whose converter does not throws <see cref="NotSupportedException"/> when used as a key.
/// </para>
/// </remarks>
public sealed class StronglyTypedIdJsonConverter<TStronglyTypedId, TValue> : JsonConverter<TStronglyTypedId>
    where TStronglyTypedId : StronglyTypedId<TValue>
    where TValue : notnull
{
    private readonly Func<TValue, TStronglyTypedId> _create = CompileConstructor();

    /// <summary>
    /// Gets <see langword="false"/>, so the serializer reads a JSON <c>null</c> as a <see langword="null"/>
    /// identifier and writes a <see langword="null"/> identifier as <c>null</c> without calling this converter.
    /// </summary>
    public override bool HandleNull => false;

    /// <summary>Reads the underlying value from JSON and wraps it in a new identifier.</summary>
    /// <param name="reader">The reader, positioned at the value.</param>
    /// <param name="typeToConvert">The identifier type being read.</param>
    /// <param name="options">The serializer options used to read the underlying value.</param>
    /// <returns>The identifier; <see langword="null"/> when the underlying value deserializes to null.</returns>
    /// <exception cref="JsonException">The JSON value cannot be read as <typeparamref name="TValue"/>.</exception>
    public override TStronglyTypedId? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = JsonSerializer.Deserialize<TValue>(ref reader, options);
        return value is null ? null : _create(value);
    }

    /// <summary>Writes the identifier's underlying value as a bare JSON value.</summary>
    /// <param name="writer">The writer. Must not be null.</param>
    /// <param name="value">The identifier to write. Must not be null.</param>
    /// <param name="options">The serializer options used to write the underlying value.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="writer"/> or <paramref name="value"/> is <see langword="null"/>.
    /// </exception>
    public override void Write(Utf8JsonWriter writer, TStronglyTypedId value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        JsonSerializer.Serialize(writer, value.Value, options);
    }

    /// <summary>Reads a dictionary key as the underlying value and wraps it in a new identifier.</summary>
    /// <param name="reader">The reader, positioned at the property name.</param>
    /// <param name="typeToConvert">The identifier type being read.</param>
    /// <param name="options">The serializer options that supply the <typeparamref name="TValue"/> converter.</param>
    /// <returns>The identifier.</returns>
    /// <exception cref="NotSupportedException">
    /// The <typeparamref name="TValue"/> converter does not support property names.
    /// </exception>
    public override TStronglyTypedId ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        _create(ValueConverter(options).ReadAsPropertyName(ref reader, typeof(TValue), options));

    /// <summary>Writes the identifier's underlying value as a dictionary key.</summary>
    /// <param name="writer">The writer. Must not be null.</param>
    /// <param name="value">The identifier to write. Must not be null.</param>
    /// <param name="options">The serializer options that supply the <typeparamref name="TValue"/> converter.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="writer"/> or <paramref name="value"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// The <typeparamref name="TValue"/> converter does not support property names.
    /// </exception>
    public override void WriteAsPropertyName(Utf8JsonWriter writer, TStronglyTypedId value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        ValueConverter(options).WriteAsPropertyName(writer, value.Value, options);
    }

    private static JsonConverter<TValue> ValueConverter(JsonSerializerOptions options) =>
        (JsonConverter<TValue>)options.GetConverter(typeof(TValue));

    private static Func<TValue, TStronglyTypedId> CompileConstructor()
    {
        var constructor = typeof(TStronglyTypedId).GetConstructor([typeof(TValue)])
            ?? throw new InvalidOperationException(
                $"'{typeof(TStronglyTypedId)}' has no public constructor taking a single '{typeof(TValue)}'. "
                + $"Declare it as 'public sealed record {typeof(TStronglyTypedId).Name}({typeof(TValue).Name} Value) "
                + $": StronglyTypedId<{typeof(TValue).Name}>(Value);'.");

        var parameter = Expression.Parameter(typeof(TValue), "value");
        return Expression.Lambda<Func<TValue, TStronglyTypedId>>(Expression.New(constructor, parameter), parameter).Compile();
    }
}
