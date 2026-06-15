using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharedKernel.Domain.StronglyTypedIds.Serialization;

/// <summary>
/// Converts a closed <see cref="StronglyTypedId{TValue}"/> type to and from its underlying
/// <typeparamref name="TValue"/> primitive, producing a bare-primitive wire format.
/// </summary>
/// <typeparam name="TStronglyTypedId">The concrete strongly-typed identifier type.</typeparam>
/// <typeparam name="TValue">The underlying primitive value type wrapped by <typeparamref name="TStronglyTypedId"/>.</typeparam>
/// <remarks>
/// <para>
/// This converter is not registered directly. Register <see cref="StronglyTypedIdJsonConverterFactory"/>
/// with <see cref="JsonSerializerOptions.Converters"/>; the factory creates the closed converter for
/// each concrete <see cref="StronglyTypedId{TValue}"/> type encountered.
/// </para>
/// <para>
/// The wire format is the bare <typeparamref name="TValue"/> — a JSON string for <see cref="Guid"/>
/// or <see cref="string"/>, a JSON number for <see cref="int"/> or <see cref="long"/> — never an
/// object wrapper such as <c>{ "value": ... }</c>.
/// </para>
/// <para>
/// <see cref="Read"/> deserializes the raw <typeparamref name="TValue"/> token and constructs
/// <typeparamref name="TStronglyTypedId"/> via a <see cref="Func{TValue, TStronglyTypedId}"/> activator
/// compiled once (via <see cref="Expression.New(System.Reflection.ConstructorInfo, IEnumerable{Expression})"/>)
/// against the concrete type's public <c>(TValue Value)</c> primary constructor and cached for the
/// lifetime of this converter instance. A concrete type that omits or hides this constructor shape
/// will throw an <see cref="InvalidOperationException"/> the first time the converter is used, not
/// when the converter is created.
/// </para>
/// </remarks>
public sealed class StronglyTypedIdJsonConverter<TStronglyTypedId, TValue> : JsonConverter<TStronglyTypedId>
    where TStronglyTypedId : StronglyTypedId<TValue>
    where TValue : notnull
{
    private static readonly Func<TValue, TStronglyTypedId> Activator = CreateActivator();

    /// <inheritdoc />
    public override TStronglyTypedId? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = JsonSerializer.Deserialize<TValue>(ref reader, options);
        return value is null ? null : Activator(value);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, TStronglyTypedId value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value.Value, options);

    /// <summary>
    /// Compiles a <see cref="Func{TValue, TStronglyTypedId}"/> activator over the public
    /// <c>(TValue Value)</c> primary constructor of <typeparamref name="TStronglyTypedId"/>.
    /// </summary>
    private static Func<TValue, TStronglyTypedId> CreateActivator()
    {
        var constructor = typeof(TStronglyTypedId).GetConstructor([typeof(TValue)])
            ?? throw new InvalidOperationException(
                $"Type '{typeof(TStronglyTypedId)}' does not declare a public constructor with a single " +
                $"parameter of type '{typeof(TValue)}'. Strongly-typed identifiers must follow the shape " +
                $"'public sealed record MyId(TValue Value) : StronglyTypedId<TValue>(Value);'.");

        var valueParameter = Expression.Parameter(typeof(TValue), "value");
        var body = Expression.New(constructor, valueParameter);

        return Expression.Lambda<Func<TValue, TStronglyTypedId>>(body, valueParameter).Compile();
    }
}
