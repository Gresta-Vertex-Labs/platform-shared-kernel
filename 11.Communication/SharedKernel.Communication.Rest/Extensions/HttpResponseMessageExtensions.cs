using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using SharedKernel.Communication.Rest.ProblemDetails;
using SharedKernel.Contracts.Envelopes;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Communication.Rest.Extensions;

/// <summary>
/// Extension methods on <see cref="HttpResponseMessage"/> for railway-oriented error handling.
/// </summary>
public static class HttpResponseMessageExtensions
{
    /// <summary>
    /// Status-check-only helper: returns <see cref="Result.Success"/> for 2xx responses, or
    /// deserializes a ProblemDetails body into <see cref="Result.Failure(Error)"/> for non-2xx
    /// responses. This method never reads or deserializes a success-path response body — its
    /// signature makes no promise about a payload. Callers that need the deserialized response body
    /// should use <see cref="ReadEnvelopeAsync{T}(HttpResponseMessage, JsonTypeInfo{T}, CancellationToken)"/>
    /// or its <see cref="JsonSerializerOptions"/> overload instead.
    /// </summary>
    /// <remarks>
    /// Supersedes the retired generic <c>EnsureSuccessOrErrorAsync&lt;T&gt;</c> (P-361/WO-056), which
    /// returned <c>Result&lt;T&gt;.Success(default!)</c> unconditionally on 2xx — a generic parameter
    /// that promised a deserialized payload the method never actually produced.
    /// </remarks>
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
