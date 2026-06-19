using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using SharedKernel.Communication.Rest.ProblemDetails;
using SharedKernel.Contracts.Envelope;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Communication.Rest.Extensions;

/// <summary>
/// Extension methods on <see cref="HttpResponseMessage"/> for railway-oriented error handling.
/// </summary>
public static class HttpResponseMessageExtensions
{
    /// <summary>
    /// Returns <c>Result.Success</c> for 2xx responses or deserializes a ProblemDetails body
    /// into <c>Result.Failure(Error)</c> for non-2xx responses.
    /// The extension does not deserialize the success payload — callers are responsible for
    /// reading the response body on success.
    /// </summary>
    /// <typeparam name="T">Expected payload type on success (used only to type the Result wrapper).</typeparam>
    public static async Task<Result<T>> EnsureSuccessOrErrorAsync<T>(
        this HttpResponseMessage response,
        CancellationToken cancellationToken = default)
    {
        if (response.IsSuccessStatusCode)
        {
            return Result<T>.Success(default!);
        }

        var error = await ProblemDetailsDeserializer
            .DeserializeAsync(response, cancellationToken)
            .ConfigureAwait(false);

        return Result<T>.Failure(error);
    }

    /// <summary>
    /// AOT-safe primary path. Deserializes a 2xx response body into <see cref="Envelope{T}.Ok(T)"/>
    /// using the provided <see cref="JsonTypeInfo{T}"/>.
    /// A 2xx response with a null or empty body returns <see cref="Envelope{T}.Fail(Error)"/>
    /// with error code <c>"http.empty-body"</c>.
    /// A non-2xx response is deserialized via <see cref="ProblemDetailsDeserializer"/> and returned
    /// as <see cref="Envelope{T}.Fail(Error)"/>.
    /// </summary>
    /// <typeparam name="T">The expected payload type on success.</typeparam>
    /// <param name="response">The HTTP response message to read.</param>
    /// <param name="typeInfo">STJ <see cref="JsonTypeInfo{T}"/> for AOT-safe deserialization.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<Envelope<T>> ReadEnvelopeAsync<T>(
        this HttpResponseMessage response,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken = default)
    {
        if (!response.IsSuccessStatusCode)
        {
            var error = await ProblemDetailsDeserializer
                .DeserializeAsync(response, cancellationToken)
                .ConfigureAwait(false);
            return Envelope<T>.Fail(error);
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(body))
        {
            return Envelope<T>.Fail(Error.Unexpected(
                "http.empty-body",
                "The 2xx response contained no body."));
        }

        var value = JsonSerializer.Deserialize(body, typeInfo);

        if (value is null)
        {
            return Envelope<T>.Fail(Error.Unexpected(
                "http.empty-body",
                "The 2xx response body deserialized to null."));
        }

        return Envelope<T>.Ok(value);
    }

    /// <summary>
    /// Reflection-based fallback overload. Same success/empty/failure logic as
    /// <see cref="ReadEnvelopeAsync{T}(HttpResponseMessage, JsonTypeInfo{T}, CancellationToken)"/>
    /// but uses reflection-based STJ deserialization.
    /// When <paramref name="options"/> is <c>null</c>, falls back to the
    /// <c>static readonly</c> <see cref="ProblemDetailsDeserializer.ReflectionFallbackOptions"/>
    /// field (case-insensitive, initialized once at class load time).
    /// Prefer the <see cref="JsonTypeInfo{T}"/> overload in AOT or performance-critical paths.
    /// </summary>
    /// <typeparam name="T">The expected payload type on success.</typeparam>
    /// <param name="response">The HTTP response message to read.</param>
    /// <param name="options">
    /// <see cref="JsonSerializerOptions"/> for deserialization. Pass <c>null</c> to use the
    /// shared reflection-based options with case-insensitive property matching.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<Envelope<T>> ReadEnvelopeAsync<T>(
        this HttpResponseMessage response,
        JsonSerializerOptions? options,
        CancellationToken cancellationToken = default)
    {
        if (!response.IsSuccessStatusCode)
        {
            var error = await ProblemDetailsDeserializer
                .DeserializeAsync(response, cancellationToken)
                .ConfigureAwait(false);
            return Envelope<T>.Fail(error);
        }

        var effectiveOptions = options ?? ProblemDetailsDeserializer.ReflectionFallbackOptions;
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(body))
        {
            return Envelope<T>.Fail(Error.Unexpected(
                "http.empty-body",
                "The 2xx response contained no body."));
        }

        var value = JsonSerializer.Deserialize<T>(body, effectiveOptions);

        if (value is null)
        {
            return Envelope<T>.Fail(Error.Unexpected(
                "http.empty-body",
                "The 2xx response body deserialized to null."));
        }

        return Envelope<T>.Ok(value);
    }
}
