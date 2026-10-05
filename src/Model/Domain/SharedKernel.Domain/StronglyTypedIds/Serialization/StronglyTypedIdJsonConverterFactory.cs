using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharedKernel.Domain.StronglyTypedIds.Serialization;

/// <summary>
/// A System.Text.Json converter factory that serializes every concrete <see cref="StronglyTypedId{TValue}"/>
/// as its bare underlying value, from a single registration.
/// </summary>
/// <remarks>
/// <para>
/// <b>Usage.</b> Add one instance to <see cref="JsonSerializerOptions.Converters"/>, as in the example. An
/// <c>OrderId</c> wrapping a <see cref="Guid"/> is then written as <c>"3f2b…"</c> rather than
/// <c>{"Value":"3f2b…"}</c>, both as a value and as a dictionary key, and a JSON <c>null</c> reads as a
/// <see langword="null"/> identifier.
/// </para>
/// <para>
/// <b>Supported types.</b> The factory handles every non-abstract, closed type that derives from
/// <see cref="StronglyTypedId{TValue}"/>, whatever underlying type System.Text.Json can serialize. Using an
/// identifier as a dictionary key additionally requires an underlying type that System.Text.Json supports as
/// a key, which every primitive, <see cref="Guid"/>, <see cref="string"/> and date type does. Each identifier
/// needs a public constructor taking its underlying value, which the positional record declaration provides.
/// </para>
/// <para>
/// <b>Pitfall.</b> The factory uses runtime reflection and compiles a constructor delegate the first time it
/// sees each identifier type, so it is not suitable for trimmed or native AOT applications.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var options = new JsonSerializerOptions();
/// options.Converters.Add(new StronglyTypedIdJsonConverterFactory());
///
/// string json = JsonSerializer.Serialize(new OrderId(Guid.CreateVersion7()), options);
/// </code>
/// </example>
public sealed class StronglyTypedIdJsonConverterFactory : JsonConverterFactory
{
    /// <summary>Returns whether <paramref name="typeToConvert"/> is a concrete strongly-typed identifier.</summary>
    /// <param name="typeToConvert">The type the serializer is about to convert.</param>
    /// <returns>
    /// <see langword="true"/> when the type is non-abstract, not an open generic definition, and derives from
    /// <see cref="StronglyTypedId{TValue}"/>; otherwise <see langword="false"/>.
    /// </returns>
    public override bool CanConvert(Type typeToConvert) => TryGetValueType(typeToConvert) is not null;

    /// <summary>
    /// Creates a <see cref="StronglyTypedIdJsonConverter{TStronglyTypedId, TValue}"/> for
    /// <paramref name="typeToConvert"/>.
    /// </summary>
    /// <param name="typeToConvert">The identifier type to create a converter for. Must not be null.</param>
    /// <param name="options">The serializer options. Not used to create the converter.</param>
    /// <returns>A converter for <paramref name="typeToConvert"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="typeToConvert"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="typeToConvert"/> is not a concrete strongly-typed identifier, or has no public
    /// constructor taking its underlying value.
    /// </exception>
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(typeToConvert);

        var valueType = TryGetValueType(typeToConvert)
            ?? throw new InvalidOperationException(
                $"'{typeToConvert}' is not a concrete StronglyTypedId<TValue> type.");

        var converterType = typeof(StronglyTypedIdJsonConverter<,>).MakeGenericType(typeToConvert, valueType);
        try
        {
            return (JsonConverter)Activator.CreateInstance(converterType)!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Throw(ex.InnerException);
            throw;
        }
    }

    private static Type? TryGetValueType(Type typeToConvert)
    {
        if (typeToConvert.IsAbstract || typeToConvert.IsGenericTypeDefinition)
            return null;

        for (var current = typeToConvert.BaseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(StronglyTypedId<>))
                return current.GetGenericArguments()[0];
        }

        return null;
    }
}
