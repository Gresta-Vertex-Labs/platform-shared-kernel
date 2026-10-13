using System.Diagnostics;
using System.Diagnostics.Metrics;
using SharedKernel.Application.Messaging;
using SharedKernel.Application.Pipeline.Shared;
using SharedKernel.Application.Pipeline.Tracing;
using SharedKernel.Primitives.Propagation;

namespace SharedKernel.Application.Pipeline.Metrics;

/// <summary>
/// Records request pipeline duration to <see cref="ApplicationMetrics"/>.
/// </summary>
/// <typeparam name="TRequest">The request type being measured.</typeparam>
/// <typeparam name="TResponse">The response type returned by the pipeline.</typeparam>
/// <remarks>
/// Records exactly once per request, tagged with <c>application.request_type</c> (full type name),
/// <c>application.request_kind</c>, <c>outcome</c> (one of <c>WellKnownOutcomes</c>: <c>success</c>, <c>failure</c>,
/// <c>exception</c>, <c>canceled</c>) and — for a <c>Result</c> failure or an exception, never for a cancellation —
/// <c>error.type</c> (the <c>ErrorType</c> name, or the thrown exception's full type name; the same text the span
/// carries), via a try/finally around <c>next()</c> so the measurement fires whether the inner pipeline succeeds,
/// returns a failed <c>Result</c>/<c>Result&lt;T&gt;</c>, or throws.
/// </remarks>
internal sealed class MetricsBehavior<TRequest, TResponse>(ApplicationMetrics metrics)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <inheritdoc/>
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerContinuation<TResponse> next,
        CancellationToken cancellationToken)
    {
        var startTimestamp = Stopwatch.GetTimestamp();

        // Pessimistic default: if next() throws, this is overwritten only by the catch below.
        var outcome = WellKnownOutcomes.Exception;
        string? errorType = null;

        try
        {
            var response = await next().ConfigureAwait(false);
            outcome = ResponseOutcome.Classify(response);
            errorType = ResponseOutcome.TryGetError(response)?.Type.ToString();
            return response;
        }
        catch (OperationCanceledException)
        {
            outcome = WellKnownOutcomes.Canceled;
            throw;
        }
        catch (Exception ex)
        {
            errorType = ex.GetType().FullName ?? ex.GetType().Name;
            throw;
        }
        finally
        {
            var elapsed = Stopwatch.GetElapsedTime(startTimestamp);
            var tags = new TagList
            {
                { ApplicationTagKeys.RequestType, typeof(TRequest).FullName ?? typeof(TRequest).Name },
                { ApplicationTagKeys.RequestKind, RequestKind.Classify<TRequest>() },
                { WellKnownTagKeys.Outcome, outcome },
            };
            if (errorType is not null)
                tags.Add(WellKnownTagKeys.ErrorType, errorType);

            metrics.RecordRequestDuration(elapsed.TotalSeconds, tags);
        }
    }
}
