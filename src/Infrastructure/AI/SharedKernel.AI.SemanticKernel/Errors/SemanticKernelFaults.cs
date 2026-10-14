using System.ClientModel;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using SharedKernel.AI.Abstractions.Errors;
using SharedKernel.AI.SemanticKernel.Diagnostics;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.AI.SemanticKernel.Errors;

/// <summary>
/// Classifies an exception raised by the OpenAI-compatible connector onto the canonical
/// <see cref="IntelligenceErrors"/> factory — the only construction path for a provider-derived <see cref="Error"/> in
/// this package.
/// </summary>
/// <remarks>
/// <para>
/// <b>Verified shapes</b> (Microsoft.SemanticKernel 1.78.0, OpenAI 2.10.0, System.ClientModel 1.10.0, through a stub
/// <c>HttpMessageHandler</c> behind the real registration): a non-success HTTP status from the chat service is a
/// <see cref="HttpOperationException"/> with <see cref="HttpOperationException.StatusCode"/> and the SDK's
/// <see cref="ClientResultException"/> as its inner exception; from the <c>EmbeddingClient</c> it is a
/// <see cref="ClientResultException"/> with <see cref="ClientResultException.Status"/>. A transport fault is the same
/// pair with no HTTP status (<c>Status</c> 0 or a <see langword="null"/> <c>StatusCode</c>) and an inner
/// <see cref="HttpRequestException"/>. An HTTP-client timeout surfaces as a <see cref="TaskCanceledException"/> whose
/// inner exception is a <see cref="TimeoutException"/>, with the caller's token not cancelled. A cancelled caller token
/// surfaces as a <see cref="TaskCanceledException"/>.
/// </para>
/// <para>
/// <b>Classify or rethrow.</b> Only a recognised provider fault becomes an <see cref="Error"/>; every call site uses an
/// exception filter, so anything else (a defect, a Semantic Kernel <see cref="KernelException"/> raised before any I/O)
/// is not caught and keeps its original stack. <b>No text</b> of the provider exception, response body or endpoint ever
/// enters an <see cref="Error"/>; the HTTP status number is the only provider token used.
/// </para>
/// </remarks>
internal static class SemanticKernelFaults
{
    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="exception"/> is a recognised provider fault or the caller's
    /// own cancellation in a non-<see cref="OperationCanceledException"/> shape.
    /// </summary>
    /// <param name="error">
    /// The mapped error, or <see langword="null"/> when the match is the caller's cancellation, which
    /// <see cref="Surface"/> turns into an <see cref="OperationCanceledException"/>.
    /// </param>
    public static bool TryClassify(
        Exception exception,
        CancellationToken cancellationToken,
        string providerName,
        string operation,
        string modelId,
        out Error? error)
    {
        error = null;

        if (cancellationToken.IsCancellationRequested)
        {
            if (exception is OperationCanceledException)
            {
                return false;
            }

            if (WrapsCancellation(exception))
            {
                return true;
            }
        }

        if (exception is OperationCanceledException or TimeoutException)
        {
            // The caller's token is live: an HTTP-client timeout, not the caller's cancellation.
            error = IntelligenceErrors.Timeout(operation);
            return true;
        }

        if (HttpStatusOf(exception) is { } status)
        {
            error = status == 0
                ? TransportOrTimeout(exception, providerName, operation)
                : FromStatus(status, providerName, operation, modelId);
            return true;
        }

        switch (exception)
        {
            // A connector exception without a status is a transport fault (no response was received).
            case HttpOperationException or ClientResultException or HttpRequestException:
                error = TransportOrTimeout(exception, providerName, operation);
                return true;

            case JsonException:
                error = IntelligenceErrors.EngineFault(providerName, operation);
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Returns the HTTP status the provider answered with (<see cref="ClientResultException.Status"/> or
    /// <see cref="HttpOperationException.StatusCode"/>), 0 when the exception is a connector exception without one, or
    /// <see langword="null"/> when <paramref name="exception"/> is not a connector exception.
    /// </summary>
    public static int? HttpStatusOf(Exception exception)
    {
        switch (exception)
        {
            case ClientResultException clientResult:
                return clientResult.Status;

            case HttpOperationException { StatusCode: { } statusCode }:
                return (int)statusCode;

            case HttpOperationException { InnerException: ClientResultException inner }:
                return inner.Status;

            case HttpOperationException:
                return 0;

            default:
                return null;
        }
    }

    /// <summary>
    /// Returns <see langword="true"/> for a real HTTP 429 or 5xx status while the caller's token is live — the only
    /// failures the opt-in bounded retry repeats. Never keyed on the mapped <see cref="Error.Type"/>.
    /// </summary>
    public static bool IsTransientHttpFailure(Exception exception, CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested && HttpStatusOf(exception) is 429 or >= 500;

    /// <summary>
    /// Completes a matched exception filter: returns the mapped <see cref="Error"/> after logging it once with the
    /// exception, or throws the caller's cancellation as <see cref="OperationCanceledException"/>.
    /// </summary>
    public static Error Surface(
        Exception exception,
        Error? error,
        CancellationToken cancellationToken,
        ILogger logger,
        string operation,
        string modelId)
    {
        if (error is null)
        {
            throw CanceledBy(exception, cancellationToken);
        }

        return Report(exception, error, logger, operation, modelId);
    }

    /// <summary>Builds the <see cref="OperationCanceledException"/> that stands for the caller's cancellation.</summary>
    public static OperationCanceledException CanceledBy(Exception exception, CancellationToken cancellationToken) =>
        new("The operation was canceled by the caller.", exception, cancellationToken);

    /// <summary>Logs a classified failure once, at Warning, with the exception, and returns <paramref name="error"/>.</summary>
    public static Error Report(Exception exception, Error error, ILogger logger, string operation, string modelId)
    {
        SemanticKernelLog.SemanticKernelEngineFault(logger, operation, modelId, error.Code, exception);
        return error;
    }

    private static bool WrapsCancellation(Exception exception)
    {
        for (var inner = exception.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (inner is OperationCanceledException)
            {
                return true;
            }
        }

        return false;
    }

    private static Error TransportOrTimeout(Exception exception, string providerName, string operation)
    {
        for (var inner = exception.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (inner is OperationCanceledException or TimeoutException)
            {
                return IntelligenceErrors.Timeout(operation);
            }
        }

        return IntelligenceErrors.Unavailable(providerName);
    }

    private static Error FromStatus(int status, string providerName, string operation, string modelId) =>
        status switch
        {
            401 or 403 => IntelligenceErrors.EngineUnauthorized(providerName, operation),
            404 => IntelligenceErrors.ModelNotFound(modelId),
            408 => IntelligenceErrors.Timeout(operation),

            // The connector version verified for this package exposes no strongly-typed Retry-After accessor, so
            // retryAfter is null, documented as a known gap rather than guessed at.
            429 => IntelligenceErrors.RateLimited(providerName, retryAfter: null),
            502 or 503 or 504 => IntelligenceErrors.Unavailable(providerName),
            >= 500 => IntelligenceErrors.EngineFault(providerName, operation, status.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            _ => IntelligenceErrors.CompletionFailed(providerName, $"The provider rejected the request with HTTP status {status}."),
        };
}
