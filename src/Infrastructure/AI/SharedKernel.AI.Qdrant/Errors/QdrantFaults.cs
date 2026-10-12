using Grpc.Core;
using Microsoft.Extensions.Logging;
using Qdrant.Client;
using SharedKernel.AI.Abstractions.Errors;
using SharedKernel.AI.Qdrant.Logging;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.AI.Qdrant.Errors;

/// <summary>
/// Classifies an exception raised by <c>Qdrant.Client</c> onto the canonical <see cref="IntelligenceErrors"/> factory
/// — the only construction path for a provider-derived <see cref="Error"/> in this package.
/// </summary>
/// <remarks>
/// <para>
/// <b>Classify or rethrow.</b> Only a recognised provider fault becomes an <see cref="Error"/>. Every call site uses
/// <c>catch (Exception ex) when (QdrantFaults.TryClassify(...))</c>, so anything else (a defect in this package, a
/// mapping bug, a <see cref="NullReferenceException"/>) is not caught at all and keeps its original stack.
/// </para>
/// <para>
/// <b>The caller's token comes first.</b> gRPC reports a cancelled call as <c>RpcException(Cancelled)</c> whose inner
/// exception is an <see cref="OperationCanceledException"/>, not as an <see cref="OperationCanceledException"/> itself
/// (VERIFIED against <c>Qdrant.Client</c> 1.18.1 on a real server, both for a pre-cancelled token and in flight). When
/// the caller's token is cancelled and the exception has that shape, <see cref="TryClassify"/> matches with a
/// <see langword="null"/> error and <see cref="Surface"/> rethrows it as <see cref="OperationCanceledException"/>.
/// </para>
/// <para>
/// <b>Text.</b> No message, <c>Status.Detail</c> or inner text of the provider exception ever enters an
/// <see cref="Error"/>; the gRPC status name (a closed enum) is the only provider token used.
/// </para>
/// </remarks>
internal static class QdrantFaults
{
    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="exception"/> is a recognised provider fault or the caller's
    /// own cancellation in a non-<see cref="OperationCanceledException"/> shape.
    /// </summary>
    /// <param name="exception">The exception being filtered.</param>
    /// <param name="cancellationToken">The caller's token.</param>
    /// <param name="providerName">The provider name for the error text.</param>
    /// <param name="operation">The operation name for the error text.</param>
    /// <param name="collectionName">The collection name for the error text.</param>
    /// <param name="error">
    /// The mapped error, or <see langword="null"/> when the match is the caller's cancellation, which
    /// <see cref="Surface"/> turns into an <see cref="OperationCanceledException"/>.
    /// </param>
    public static bool TryClassify(
        Exception exception,
        CancellationToken cancellationToken,
        string providerName,
        string operation,
        string collectionName,
        out Error? error)
    {
        error = null;

        if (cancellationToken.IsCancellationRequested)
        {
            // A plain OperationCanceledException already is what the caller expects; let it propagate untouched.
            if (exception is OperationCanceledException)
            {
                return false;
            }

            if (IsCancellationShaped(exception))
            {
                return true;
            }
        }

        switch (exception)
        {
            case RpcException rpc:
                error = FromRpcException(rpc, providerName, operation, collectionName);
                return true;

            // VERIFIED (Qdrant.Client 1.18.1): QdrantException is the client library's own exception for failures it
            // cannot express as a gRPC status. It is a provider fault, never a defect in this package.
            case QdrantException:
                error = IntelligenceErrors.EngineFault(providerName, operation);
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Returns <see langword="true"/> when the caller's token is cancelled and <paramref name="exception"/> is a gRPC
    /// cancellation or wraps an <see cref="OperationCanceledException"/>, but is not one itself.
    /// </summary>
    public static bool IsCancellationOf(Exception exception, CancellationToken cancellationToken) =>
        cancellationToken.IsCancellationRequested
        && exception is not OperationCanceledException
        && IsCancellationShaped(exception);

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
        string collectionName)
    {
        if (error is null)
        {
            throw CanceledBy(exception, cancellationToken);
        }

        return Report(exception, error, logger, operation, collectionName);
    }

    /// <summary>Builds the <see cref="OperationCanceledException"/> that stands for the caller's cancellation.</summary>
    public static OperationCanceledException CanceledBy(Exception exception, CancellationToken cancellationToken) =>
        new("The operation was canceled by the caller.", exception, cancellationToken);

    /// <summary>Logs a classified failure once, at Warning, with the exception, and returns <paramref name="error"/>.</summary>
    public static Error Report(Exception exception, Error error, ILogger logger, string operation, string collectionName)
    {
        logger.QdrantEngineFault(operation, collectionName, error.Code, exception);
        return error;
    }

    private static bool IsCancellationShaped(Exception exception)
    {
        if (exception is RpcException { StatusCode: StatusCode.Cancelled })
        {
            return true;
        }

        for (var inner = exception.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (inner is OperationCanceledException)
            {
                return true;
            }
        }

        return false;
    }

    private static Error FromRpcException(RpcException rpc, string providerName, string operation, string collectionName) =>
        rpc.StatusCode switch
        {
            StatusCode.NotFound => IntelligenceErrors.CollectionNotFound(collectionName),
            StatusCode.AlreadyExists => IntelligenceErrors.CollectionAlreadyExists(collectionName),
            StatusCode.InvalidArgument or StatusCode.FailedPrecondition =>
                IntelligenceErrors.InvalidQuery($"The engine rejected the request as invalid (gRPC {rpc.StatusCode})."),
            StatusCode.PermissionDenied or StatusCode.Unauthenticated => IntelligenceErrors.Unauthorized(collectionName, operation),
            StatusCode.ResourceExhausted => IntelligenceErrors.RateLimited(providerName, retryAfter: null),
            StatusCode.Unavailable => IntelligenceErrors.Unreachable(providerName),
            StatusCode.DeadlineExceeded => IntelligenceErrors.Timeout(operation),

            // The closed StatusCode enum names the rest (Internal, Unknown, DataLoss, Unimplemented, Aborted,
            // OutOfRange and, with the caller's token live, Cancelled), so the name is a bounded token.
            _ => IntelligenceErrors.EngineFault(providerName, operation, rpc.StatusCode.ToString()),
        };
}
