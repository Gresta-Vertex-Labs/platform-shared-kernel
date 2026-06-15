using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharedKernel.Domain.StronglyTypedIds.Serialization;

/// <summary>
/// A <see cref="JsonConverterFactory"/> that produces bare-primitive converters for concrete
/// <see cref="StronglyTypedId{TValue}"/> types.
/// </summary>
/// <remarks>
/// <para>
/// Supports exactly four <c>TValue</c> shapes: <see cref="Guid"/>, <see cref="int"/>, <see cref="long"/>,
/// and <see cref="string"/>. A closed <see cref="StronglyTypedId{TValue}"/> for any other
/// <c>TValue</c> is not converted by this factory — <see cref="CanConvert"/> returns <c>false</c> and
/// such types fall back to default System.Text.Json record serialization (an object wrapper
/// <c>{ "value": ... }</c>) unless the consuming service registers its own converter.
/// </para>
/// <para>
/// This factory is opt-in. <c>SharedKernel.Domain</c> does not call
/// <see cref="JsonSerializerOptions.Converters"/> anywhere itself and ships no global System.Text.Json
/// configuration. Consuming services register it explicitly:
/// <code>
/// var options = new JsonSerializerOptions();
/// options.Converters.Add(new StronglyTypedIdJsonConverterFactory());
/// // OrderId, CustomerId, etc. now (de)serialize as their bare TValue.
/// </code>
/// </para>
/// </remarks>
public sealed class StronglyTypedIdJsonConverterFactory : JsonConverterFactory
{
    private static readonly Type[] SupportedValueTypes = [typeof(Guid), typeof(int), typeof(long), typeof(string)];

    /// <inheritdoc />
    public override bool CanConvert(Type typeToConvert) => TryGetValueType(typeToConvert) is not null;

    /// <inheritdoc />
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var valueType = TryGetValueType(typeToConvert)
            ?? throw new InvalidOperationException(
                $"Type '{typeToConvert}' is not a supported closed StronglyTypedId<TValue> type.");

        var converterType = typeof(StronglyTypedIdJsonConverter<,>).MakeGenericType(typeToConvert, valueType);

        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }

    /// <summary>
    /// Walks <paramref name="typeToConvert"/>'s base type chain looking for a closed
    /// <see cref="StronglyTypedId{TValue}"/> whose <c>TValue</c> is one of the supported shapes.
    /// </summary>
    /// <param name="typeToConvert">The candidate type.</param>
    /// <returns>The closed <c>TValue</c> type if <paramref name="typeToConvert"/> qualifies; otherwise <see langword="null"/>.</returns>
    private static Type? TryGetValueType(Type typeToConvert)
    {
        if (typeToConvert.IsAbstract)
        {
            return null;
        }

        for (var current = typeToConvert.BaseType; current is not null; current = current.BaseType)
        {
            if (!current.IsGenericType || current.GetGenericTypeDefinition() != typeof(StronglyTypedId<>))
            {
                continue;
            }

            var valueType = current.GetGenericArguments()[0];
            return Array.IndexOf(SupportedValueTypes, valueType) >= 0 ? valueType : null;
        }

        return null;
    }
}
