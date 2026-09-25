using System.Text.Json;
using System.Text.Json.Serialization;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Application.Idempotency;

namespace SharedKernel.Application.Pipeline;

/// <summary>
/// Serializes and deserializes pipeline responses (<see cref="Result"/> / <see cref="Result{T}"/>)
/// for <see cref="IRequestIdempotencyStore"/>-backed response replay.
/// </summary>
/// <remarks>
/// This class owns the (de)serialization; a store persists whatever opaque string it is handed and
/// never needs to parse or interpret it. <see cref="Result"/> and <see cref="Result{T}"/> both have
/// private constructors and throw on wrong-state member access, so neither round-trips through
/// <see cref="System.Text.Json"/>'s default reflection-based contract — two small custom converters
/// bridge this. <see cref="Error"/> itself (including its <see cref="Error.Details"/> child-error
/// list) round-trips through STJ's default record support with no custom converter needed.
/// </remarks>
internal static class IdempotencyResponseSerializer
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    /// <summary>Serializes <paramref name="response"/> (a closed <see cref="Result"/>/<see cref="Result{T}"/>) to a string.</summary>
    internal static string Serialize<TResponse>(TResponse response)
        => JsonSerializer.Serialize(response, typeof(TResponse), Options);

    /// <summary>Deserializes a previously-<see cref="Serialize{TResponse}"/>d response.</summary>
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
    // asks to convert — the standard System.Text.Json extension point for open-generic types.
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
