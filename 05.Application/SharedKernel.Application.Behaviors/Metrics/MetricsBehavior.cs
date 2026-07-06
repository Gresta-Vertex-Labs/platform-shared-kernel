using System.Diagnostics;
using System.Diagnostics.Metrics;
using MediatR;
using SharedKernel.Application.Behaviors.Shared;

namespace SharedKernel.Application.Behaviors.Metrics;

/// <summary>
/// Records request pipeline duration to <see cref="ApplicationDiagnostics.RequestDuration"/>.
/// </summary>
/// <typeparam name="TRequest">The request type being measured.</typeparam>
/// <typeparam name="TResponse">The response type returned by the pipeline.</typeparam>
/// <remarks>
/// Records <see cref="ApplicationDiagnostics.RequestDuration"/> exactly once per request, tagged
/// with <c>request.name = typeof(TRequest).FullName ?? typeof(TRequest).Name</c> (FullName
/// preferred to prevent metric key collisions when two assemblies in the same host define a request
/// type with the same short name), via a try/finally around <c>next()</c> so the measurement is
/// recorded whether the inner pipeline succeeds, returns a failed
/// <c>Result</c>/<c>Result&lt;T&gt;</c>, or throws.
/// <para>
/// <b>Outcome tag (WO-039, P-239):</b> every recorded measurement additionally carries an
/// <c>outcome</c> tag of <c>"success"</c>, <c>"failure"</c> (response is a <c>Result</c>/<c>Result&lt;T&gt;</c>
/// with <c>IsSuccess == false</c>), or <c>"exception"</c> (the inner pipeline threw before producing
/// a response) — mirroring <see cref="Streaming.StreamMetricsBehavior{TRequest,TResponse}"/>'s
/// existing <c>"streamed"</c>/<c>"faulted"</c> outcome tag on the streaming side. Classification is
/// computed via <see cref="ResponseOutcomeClassifier"/>, the same helper
/// <see cref="Logging.LoggingBehavior{TRequest,TResponse}"/> uses, so the two behaviors' taxonomy
/// never silently diverges.
/// </para>
/// </remarks>
public sealed class MetricsBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <inheritdoc/>
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var startTimestamp = Stopwatch.GetTimestamp();

        // Pessimistic default: if next() throws, this value is never overwritten and "exception"
        // is recorded. Overwritten with the classified success/failure outcome only after next()
        // returns normally.
        var outcome = ResponseOutcomeClassifier.Exception;

        try
        {
            var response = await next().ConfigureAwait(false);
            outcome = ResponseOutcomeClassifier.Classify(response);
            return response;
        }
        finally
        {
            var elapsed = Stopwatch.GetElapsedTime(startTimestamp);
            ApplicationDiagnostics.RequestDuration.Record(
                elapsed.TotalMilliseconds,
                new TagList
                {
                    { "request.name", typeof(TRequest).FullName ?? typeof(TRequest).Name },
                    { "outcome", outcome }
                });
        }
    }
}
