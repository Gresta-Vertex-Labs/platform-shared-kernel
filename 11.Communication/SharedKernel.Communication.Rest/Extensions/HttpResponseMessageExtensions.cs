using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using SharedKernel.Communication.Rest.ProblemDetails;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Communication.Rest.Extensions;

/// <summary>
/// Extension methods on <see cref="HttpResponseMessage"/> for railway-oriented error handling.
/// </summary>
/// <remarks>
/// The wire format for a failed call is an RFC 9457 ProblemDetails body. These helpers map it back
/// to an in-process <see cref="Error"/>, so a typed client returns <see cref="Result"/> or
/// <see cref="Result{T}"/> exactly as the handler on the other side of the call did.
/// </remarks>
public static class HttpResponseMessageExtensions
{
    private const string EmptyBodyCode = "http.empty-body";

    /// <summary>
    /// Status-check-only helper: returns <see cref="Result.Success"/> for 2xx responses, or
    /// deserializes a ProblemDetails body into <see cref="Result.Failure(Error)"/> for non-2xx
    /// responses. This method never reads or deserializes a success-path response body — its
    /// signature makes no promise about a payload. Callers that need the deserialized response body
    /// should use <see cref="ReadResultAsync{T}(HttpResponseMessage, JsonTypeInfo{T}, CancellationToken)"/>
    /// or its <see cref="JsonSerializerOptions"/> overload instead.
    /// </summary>
    /// <remarks>
    /// Supersedes the retired generic <c>EnsureSuccessOrErrorAsync&lt;T&gt;</c>, which returned
    /// <c>Result&lt;T&gt;.Success(default!)</c> unconditionally on 2xx — a generic parameter that
    /// promised a deserialized payload the method never actually produced.
    /// </remarks>
    /// <param name="response">The HTTP response message to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A successful <see cref="Result"/> for 2xx; otherwise a failure carrying the ProblemDetails-derived <see cref="Error"/>.</returns>
    public static async Task<Result> EnsureSuccessOrErrorAsync(
        this HttpResponseMessage response,
        CancellationToken cancellationToken = default)
    {
        if (response.IsSuccessStatusCode)
        {
            return Result.Success();
        }

        var error = await ProblemDetailsDeserializer
            .DeserializeAsync(response, cancellationToken)
            .ConfigureAwait(false);

        return Result.Failure(error);
    }

    /// <summary>
    /// Deserializes a 2xx response body into a successful <see cref="Result{T}"/> using the provided
    /// <see cref="JsonTypeInfo{T}"/>.
    /// A 2xx response with an empty or whitespace body, or a body that deserializes to
    /// <see langword="null"/>, returns a failure with error code <c>"http.empty-body"</c>.
    /// A non-2xx response is deserialized via <see cref="ProblemDetailsDeserializer"/> and returned
    /// as a failure.
    /// </summary>
    /// <typeparam name="T">The expected payload type on success.</typeparam>
    /// <param name="response">The HTTP response message to read.</param>
    /// <param name="typeInfo">STJ <see cref="JsonTypeInfo{T}"/>, typically from a source-generated context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The deserialized payload, or the <see cref="Error"/> describing why none could be read.</returns>
    /// <exception cref="JsonException">The 2xx response body is not valid JSON for <typeparamref name="T"/>.</exception>
    public static async Task<Result<T>> ReadResultAsync<T>(
        this HttpResponseMessage response,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken = default)
    {
        if (!response.IsSuccessStatusCode)
        {
            var error = await ProblemDetailsDeserializer
                .DeserializeAsync(response, cancellationToken)
                .ConfigureAwait(false);
            return Result<T>.Failure(error);
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(body))
        {
            return Result<T>.Failure(EmptyBody());
        }

        var value = JsonSerializer.Deserialize(body, typeInfo);

        return value is null
            ? Result<T>.Failure(NullBody())
            : Result<T>.Success(value);
    }

    /// <summary>
    /// Reflection-based overload. Same success/empty/failure logic as
    /// <see cref="ReadResultAsync{T}(HttpResponseMessage, JsonTypeInfo{T}, CancellationToken)"/>
    /// but uses reflection-based STJ deserialization.
    /// When <paramref name="options"/> is <c>null</c>, falls back to the
    /// <c>static readonly</c> <see cref="ProblemDetailsDeserializer.ReflectionFallbackOptions"/>
    /// field (case-insensitive, initialized once at class load time).
    /// </summary>
    /// <typeparam name="T">The expected payload type on success.</typeparam>
    /// <param name="response">The HTTP response message to read.</param>
    /// <param name="options">
    /// <see cref="JsonSerializerOptions"/> for deserialization. Pass <c>null</c> to use the
    /// shared reflection-based options with case-insensitive property matching.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The deserialized payload, or the <see cref="Error"/> describing why none could be read.</returns>
    /// <exception cref="JsonException">The 2xx response body is not valid JSON for <typeparamref name="T"/>.</exception>
    public static async Task<Result<T>> ReadResultAsync<T>(
        this HttpResponseMessage response,
        JsonSerializerOptions? options,
        CancellationToken cancellationToken = default)
    {
        if (!response.IsSuccessStatusCode)
        {
            var error = await ProblemDetailsDeserializer
                .DeserializeAsync(response, cancellationToken)
                .ConfigureAwait(false);
            return Result<T>.Failure(error);
        }

        var effectiveOptions = options ?? ProblemDetailsDeserializer.ReflectionFallbackOptions;
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(body))
        {
            return Result<T>.Failure(EmptyBody());
        }

        var value = JsonSerializer.Deserialize<T>(body, effectiveOptions);

        return value is null
            ? Result<T>.Failure(NullBody())
            : Result<T>.Success(value);
    }

    private static Error EmptyBody() =>
        Error.Unexpected(EmptyBodyCode, "The 2xx response contained no body.");

    private static Error NullBody() =>
        Error.Unexpected(EmptyBodyCode, "The 2xx response body deserialized to null.");
}
