using System.Diagnostics;
using MediatR;

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

        try
        {
            return await next().ConfigureAwait(false);
        }
        finally
        {
            var elapsed = Stopwatch.GetElapsedTime(startTimestamp);
            ApplicationDiagnostics.RequestDuration.Record(
                elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("request.name", typeof(TRequest).FullName ?? typeof(TRequest).Name));
        }
    }
}
