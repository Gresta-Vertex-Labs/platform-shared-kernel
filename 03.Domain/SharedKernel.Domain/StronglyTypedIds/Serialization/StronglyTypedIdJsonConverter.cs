using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharedKernel.Domain.StronglyTypedIds.Serialization;

/// <summary>
/// Serializes one strongly-typed identifier type as its bare underlying value, including as a dictionary key.
/// </summary>
/// <typeparam name="TStronglyTypedId">The identifier type.</typeparam>
/// <typeparam name="TValue">The identifier's underlying value type.</typeparam>
/// <remarks>
/// Normally created by <see cref="StronglyTypedIdJsonConverterFactory"/>. Apply it directly with
/// <c>[JsonConverter(typeof(StronglyTypedIdJsonConverter&lt;OrderId, Guid&gt;))]</c> only to cover a single
/// identifier type without registering the factory.
/// </remarks>
public sealed class StronglyTypedIdJsonConverter<TStronglyTypedId, TValue> : JsonConverter<TStronglyTypedId>
    where TStronglyTypedId : StronglyTypedId<TValue>
    where TValue : notnull
{
    private readonly Func<TValue, TStronglyTypedId> _create = CompileConstructor();

    /// <inheritdoc/>
    public override bool HandleNull => false;

    /// <inheritdoc/>
    public override TStronglyTypedId? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = JsonSerializer.Deserialize<TValue>(ref reader, options);
        return value is null ? null : _create(value);
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, TStronglyTypedId value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        JsonSerializer.Serialize(writer, value.Value, options);
    }

    /// <inheritdoc/>
    public override TStronglyTypedId ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        _create(ValueConverter(options).ReadAsPropertyName(ref reader, typeof(TValue), options));

    /// <inheritdoc/>
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
