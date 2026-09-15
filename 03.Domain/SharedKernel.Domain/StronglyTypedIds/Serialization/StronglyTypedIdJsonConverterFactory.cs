using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharedKernel.Domain.StronglyTypedIds.Serialization;

/// <summary>
/// Serializes every concrete <see cref="StronglyTypedId{TValue}"/> as its bare underlying value, from a single
/// registration.
/// </summary>
/// <remarks>
/// <para>
/// Register it once and every identifier type is covered: an <c>OrderId</c> wrapping a <see cref="Guid"/> is
/// written as <c>"3f2b…"</c>, not <c>{"Value":"3f2b…"}</c>. It also works for identifiers used as dictionary
/// keys.
/// </para>
/// <code>
/// var options = new JsonSerializerOptions();
/// options.Converters.Add(new StronglyTypedIdJsonConverterFactory());
/// </code>
/// <para>
/// Any underlying type System.Text.Json can serialize is supported. An identifier used as a dictionary key
/// additionally needs an underlying type that System.Text.Json supports as a key, which every primitive,
/// <see cref="Guid"/>, <see cref="string"/> and date type does.
/// </para>
/// <para>
/// A JSON <c>null</c> reads as a <see langword="null"/> identifier. Each identifier needs a public constructor
/// taking the underlying value, which the positional record declaration provides.
/// </para>
/// <para>
/// Uses runtime reflection to build a converter per identifier type the first time it is seen; not intended
/// for trimmed or native AOT applications.
/// </para>
/// </remarks>
public sealed class StronglyTypedIdJsonConverterFactory : JsonConverterFactory
{
    /// <inheritdoc/>
    public override bool CanConvert(Type typeToConvert) => TryGetValueType(typeToConvert) is not null;

    /// <inheritdoc/>
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
