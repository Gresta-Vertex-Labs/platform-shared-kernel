using System.Diagnostics;
using System.Diagnostics.Metrics;
using MediatR;
using SharedKernel.Application.Behaviors.Shared;

namespace SharedKernel.Application.Behaviors.Metrics;

/// <summary>
/// Records request pipeline duration to <see cref="ApplicationMetrics"/>.
/// </summary>
/// <typeparam name="TRequest">The request type being measured.</typeparam>
/// <typeparam name="TResponse">The response type returned by the pipeline.</typeparam>
/// <remarks>
/// Records exactly once per request, tagged with <c>request.type</c> (full type name),
/// <c>request.kind</c>, <c>outcome</c> (<c>"success"</c>/<c>"failure"</c>/<c>"exception"</c>), and —
/// whenever the outcome is not a success — <c>error.type</c> (the <c>Error.Type</c> name for a
/// <c>Result</c> failure, or the thrown exception's full type name), via a try/finally around
/// <c>next()</c> so the measurement fires whether the inner pipeline succeeds, returns a failed
/// <c>Result</c>/<c>Result&lt;T&gt;</c>, or throws.
/// </remarks>
internal sealed class MetricsBehavior<TRequest, TResponse>(ApplicationMetrics metrics)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <inheritdoc/>
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var startTimestamp = Stopwatch.GetTimestamp();

        // Pessimistic default: if next() throws, this is never overwritten and "exception" is
        // recorded with the exception's own type name as error.type.
        var outcome = ResponseOutcome.Exception;
        string? errorType = null;

        try
        {
            var response = await next().ConfigureAwait(false);
            outcome = ResponseOutcome.Classify(response);
            errorType = ResponseOutcome.TryGetError(response)?.Type.ToString();
            return response;
        }
        catch (Exception ex)
        {
            errorType = ex.GetType().FullName;
            throw;
        }
        finally
        {
            var elapsed = Stopwatch.GetElapsedTime(startTimestamp);
            var tags = new TagList
            {
                { "request.type", typeof(TRequest).FullName ?? typeof(TRequest).Name },
                { "request.kind", RequestKind.Classify<TRequest>() },
                { "outcome", outcome },
            };
            if (errorType is not null)
                tags.Add("error.type", errorType);

            metrics.RecordRequestDuration(elapsed.TotalSeconds, tags);
        }
    }
}
