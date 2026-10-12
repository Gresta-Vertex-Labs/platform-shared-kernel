using Microsoft.Extensions.Logging;
using SharedKernel.AI.Abstractions.Constants;
using SharedKernel.AI.Qdrant.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.AI.Qdrant.Diagnostics;

/// <summary>
/// Runs one data-plane operation inside its span and duration observation, and applies the classify-or-rethrow policy
/// in one place.
/// </summary>
/// <remarks>
/// A recognised provider fault becomes a failed <see cref="Result{T}"/> (logged once, at Warning, with the exception);
/// the caller's cancellation surfaces as <see cref="OperationCanceledException"/> even when gRPC reports it as
/// <c>RpcException(Cancelled)</c>; anything else propagates with its original stack and is recorded on the span as an
/// exception. A pre-I/O rejection returned by <paramref name="body"/> still yields a span and a duration observation.
/// </remarks>
internal static class QdrantOperationRunner
{
    public static async Task<Result<T>> RunAsync<T>(
        QdrantTelemetry telemetry,
        ILogger logger,
        string operation,
        string collectionName,
        CancellationToken cancellationToken,
        Func<Task<Result<T>>> body)
    {
        using var scope = telemetry.Begin(operation, collectionName);
        try
        {
            Result<T> result;
            try
            {
                result = await body().ConfigureAwait(false);
            }
            catch (Exception ex) when (QdrantFaults.TryClassify(
                ex, cancellationToken, IntelligenceWellKnown.QdrantProviderName, operation, collectionName, out var error))
            {
                result = Result<T>.Failure(QdrantFaults.Surface(ex, error, cancellationToken, logger, operation, collectionName));
            }

            return scope.Complete(result);
        }
        catch (Exception ex)
        {
            scope.Fail(ex);
            throw;
        }
    }
}
