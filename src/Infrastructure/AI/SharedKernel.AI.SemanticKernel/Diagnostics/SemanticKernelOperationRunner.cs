using Microsoft.Extensions.Logging;
using SharedKernel.AI.Abstractions.Constants;
using SharedKernel.AI.SemanticKernel.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.AI.SemanticKernel.Diagnostics;

/// <summary>
/// Runs one non-streaming operation inside its span and duration observation, and applies the classify-or-rethrow
/// policy in one place.
/// </summary>
/// <remarks>
/// A recognised provider fault becomes a failed <see cref="Result{T}"/> (logged once, at Warning, with the exception);
/// the caller's cancellation surfaces as <see cref="OperationCanceledException"/>; anything else propagates with its
/// original stack and is recorded on the span as an exception. A pre-I/O rejection returned by the body still yields a
/// span and a duration observation.
/// </remarks>
internal static class SemanticKernelOperationRunner
{
    public static async Task<Result<T>> RunAsync<T>(
        SemanticKernelTelemetry telemetry,
        ILogger logger,
        string operation,
        string boundedModel,
        string modelId,
        CancellationToken cancellationToken,
        Func<Task<Result<T>>> body)
    {
        using var scope = telemetry.Begin(operation, boundedModel);
        try
        {
            Result<T> result;
            try
            {
                result = await body().ConfigureAwait(false);
            }
            catch (Exception ex) when (SemanticKernelFaults.TryClassify(
                ex, cancellationToken, IntelligenceWellKnown.SemanticKernelProviderName, operation, modelId, out var error))
            {
                result = Result<T>.Failure(SemanticKernelFaults.Surface(ex, error, cancellationToken, logger, operation, modelId));
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
