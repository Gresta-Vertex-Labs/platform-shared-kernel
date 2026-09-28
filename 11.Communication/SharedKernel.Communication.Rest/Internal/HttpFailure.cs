using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Polly.CircuitBreaker;
using Polly.Timeout;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Communication.Rest.Internal;

/// <summary>Turns a response, or the exception thrown instead of one, into a <see cref="Result"/>.</summary>
internal static class HttpFailure
{
    /// <summary>
    /// The error for an exception thrown by a send: the service unreachable, a timeout, an open circuit, no access
    /// token. <see langword="false"/> for the caller's own cancellation and for anything else, which are not results.
    /// </summary>
    public static bool TryMap(Exception exception, CancellationToken cancellationToken, [NotNullWhen(true)] out Error? error)
    {
        error = exception switch
        {
            OperationCanceledException when cancellationToken.IsCancellationRequested => null,
            AccessTokenUnavailableException e => e.Error,
            BrokenCircuitException => Error.Unavailable(
                CommunicationErrorCodes.CircuitOpen,
                "The service is failing; the call was not sent while its circuit breaker is open."),
            TimeoutRejectedException or OperationCanceledException => Error.Timeout(
                CommunicationErrorCodes.Timeout,
                "The service did not answer in time."),
            HttpRequestException e => Error.Unavailable(
                CommunicationErrorCodes.Unreachable,
                $"The service could not be reached ({e.HttpRequestError})."),
            _ => null,
        };

        return error is not null;
    }

    /// <summary>A 2xx is success; anything else is the error its body or status describes.</summary>
    public static async Task<Result> ToResultAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        response.IsSuccessStatusCode
            ? Result.Success()
            : Result.Failure(await ProblemDetailsDeserializer.DeserializeAsync(response, cancellationToken).ConfigureAwait(false));

    /// <summary>A 2xx body read as <typeparamref name="T"/>; anything else is the error its body or status describes.</summary>
    public static async Task<Result<T>> ReadAsync<T>(
        HttpResponseMessage response,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            return Result<T>.Failure(await ProblemDetailsDeserializer.DeserializeAsync(response, cancellationToken).ConfigureAwait(false));
        }

        if (response.Content.Headers.ContentLength is null)
        {
            // A chunked body: buffered, so an empty one can be told from one that is not JSON.
            await response.Content.LoadIntoBufferAsync(cancellationToken).ConfigureAwait(false);
        }

        if (response.StatusCode == HttpStatusCode.NoContent || response.Content.Headers.ContentLength == 0)
        {
            return Result<T>.Failure(EmptyBody());
        }

        try
        {
            Stream body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            T? value = await JsonSerializer.DeserializeAsync(body, typeInfo, cancellationToken).ConfigureAwait(false);
            return value is null ? Result<T>.Failure(EmptyBody()) : Result<T>.Success(value);
        }
        catch (JsonException)
        {
            return Result<T>.Failure(Error.Unexpected(
                CommunicationErrorCodes.InvalidBody,
                $"The response body is not valid JSON for {typeof(T).Name}."));
        }
    }

    private static Error EmptyBody() =>
        Error.Unexpected(CommunicationErrorCodes.EmptyBody, "The success response has no body.");
}
