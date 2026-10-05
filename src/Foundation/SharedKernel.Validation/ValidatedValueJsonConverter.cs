using System.Text.Json;
using System.Text.Json.Serialization;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Validation;

/// <summary>
/// Reads and writes an <see cref="IValidatedValue{TSelf}"/> as a JSON string. Every identifier type
/// in this package already carries it through <see cref="JsonConverterAttribute"/>, so there is
/// nothing to register.
/// </summary>
/// <typeparam name="T">The identifier type.</typeparam>
/// <remarks>
/// <para>
/// Reading validates: a string that is not a valid value throws <see cref="JsonException"/> with the
/// validation message, so an invalid identifier can never be deserialized into a request object.
/// A JSON <c>null</c> is only accepted for a nullable property.
/// </para>
/// <para>
/// Writing emits <see cref="IValidatedValue{TSelf}.Value"/>, the full normalized value. For
/// <see cref="CardNumber"/> that is the full card number: serialize it only where the full number
/// is meant to go.
/// </para>
/// </remarks>
public sealed class ValidatedValueJsonConverter<T> : JsonConverter<T>
    where T : struct, IValidatedValue<T>
{
    /// <inheritdoc />
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"Expected a JSON string for {typeof(T).Name}, found {reader.TokenType}.");
        }

        Result<T> result = T.Create(reader.GetString());
        return result.IsSuccess ? result.Value : throw new JsonException(result.Error.Message);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value.Value);
    }
}
