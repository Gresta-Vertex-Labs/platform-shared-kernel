using System.Text.Json;
using System.Text.Json.Serialization;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application.Behaviors.Idempotency;

/// <summary>
/// Serializes and deserializes pipeline responses (<see cref="Result"/> / <see cref="Result{T}"/>)
/// for <see cref="IIdempotencyResponseStore"/>-backed idempotency response replay (WO-039, P-242).
/// </summary>
/// <remarks>
/// <para>
/// This class owns the (de)serialization; a store implementing <see cref="IIdempotencyResponseStore"/>
/// persists whatever opaque string it is handed and never needs to parse or interpret it.
/// </para>
/// <para>
/// <see cref="Result"/> and <see cref="Result{T}"/> both have private constructors and throw on
/// wrong-state member access (<c>Value</c> on a failure, <c>Error</c> on a success), so neither
/// round-trips through <see cref="System.Text.Json"/>'s default reflection-based contract. Two small
/// custom converters (<see cref="ResultJsonConverter"/>, <see cref="ResultOfTJsonConverterFactory"/>)
/// bridge this — the same <see cref="JsonConverterFactory"/> pattern already used by
/// <c>03.Domain</c>'s <c>StronglyTypedIdJsonConverterFactory</c> for an equivalent open-generic-type
/// serialization problem, not a new or ad hoc mechanism.
/// </para>
/// </remarks>
internal static class IdempotencyResponseSerializer
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    /// <summary>Serializes <paramref name="response"/> (a closed <see cref="Result"/>/<see cref="Result{T}"/>) to a string.</summary>
    /// <typeparam name="TResponse">The response type, statically known only as <c>IRequest&lt;TResponse&gt;</c>'s response.</typeparam>
    /// <param name="response">The response instance to serialize.</param>
    internal static string Serialize<TResponse>(TResponse response)
        => JsonSerializer.Serialize(response, typeof(TResponse), Options);

    /// <summary>Deserializes a previously-<see cref="Serialize{TResponse}"/>d response.</summary>
    /// <typeparam name="TResponse">The response type to deserialize into.</typeparam>
    /// <param name="serializedResponse">The string produced by a prior <see cref="Serialize{TResponse}"/> call.</param>
    internal static TResponse Deserialize<TResponse>(string serializedResponse)
        => (TResponse)JsonSerializer.Deserialize(serializedResponse, typeof(TResponse), Options)!;

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new ResultJsonConverter());
        options.Converters.Add(new ResultOfTJsonConverterFactory());
        return options;
    }

    // Result (non-generic, readonly struct) carries no typed Value — only IsSuccess and,
    // conditionally, Error.
    private sealed class ResultJsonConverter : JsonConverter<Result>
    {
        public override Result Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;

            return root.GetProperty("isSuccess").GetBoolean()
                ? Result.Success()
                : Result.Failure(root.GetProperty("error").Deserialize<Error>(options)!);
        }

        public override void Write(Utf8JsonWriter writer, Result value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteBoolean("isSuccess", value.IsSuccess);
            if (value.IsFailure)
            {
                writer.WritePropertyName("error");
                JsonSerializer.Serialize(writer, value.Error, options);
            }
            writer.WriteEndObject();
        }
    }

    // Result<T> carries a typed Value on success, an Error on failure.
    private sealed class ResultOfTJsonConverter<T> : JsonConverter<Result<T>>
    {
        public override Result<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;

            return root.GetProperty("isSuccess").GetBoolean()
                ? Result<T>.Success(root.GetProperty("value").Deserialize<T>(options)!)
                : Result<T>.Failure(root.GetProperty("error").Deserialize<Error>(options)!);
        }

        public override void Write(Utf8JsonWriter writer, Result<T> value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteBoolean("isSuccess", value.IsSuccess);
            writer.WritePropertyName(value.IsSuccess ? "value" : "error");
            if (value.IsSuccess)
                JsonSerializer.Serialize(writer, value.Value, options);
            else
                JsonSerializer.Serialize(writer, value.Error, options);
            writer.WriteEndObject();
        }
    }

    // Open-generic factory resolving ResultOfTJsonConverter<T> for whichever closed Result<T> STJ
    // asks to convert — the standard System.Text.Json extension point for open-generic types,
    // already established in this platform by 03.Domain's StronglyTypedIdJsonConverterFactory.
    private sealed class ResultOfTJsonConverterFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert)
            => typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(Result<>);

        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        {
            var innerType = typeToConvert.GetGenericArguments()[0];
            var converterType = typeof(ResultOfTJsonConverter<>).MakeGenericType(innerType);
            return (JsonConverter)Activator.CreateInstance(converterType)!;
        }
    }
}
