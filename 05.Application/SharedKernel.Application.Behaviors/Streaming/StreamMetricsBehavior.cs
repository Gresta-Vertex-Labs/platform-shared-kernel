using MediatR;
using SharedKernel.Application.Behaviors.Metrics;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;

namespace SharedKernel.Application.Behaviors.Streaming;

/// <summary>
/// Records a single <c>sharedkernel.application.request.duration</c> measurement per stream,
/// tagged with the request type name and an <c>outcome</c> tag (<c>"streamed"</c> for normal
/// completion, <c>"faulted"</c> when an exception terminates the stream).
/// </summary>
/// <typeparam name="TRequest">The streaming request type.</typeparam>
/// <typeparam name="TResponse">The per-item payload type yielded by the stream.</typeparam>
/// <remarks>
/// The measurement always fires — whether the stream completes normally or throws. Uses
/// <see cref="Stopwatch.GetTimestamp"/> and <c>Stopwatch.GetElapsedTime</c> (zero allocation).
/// </remarks>
public sealed class StreamMetricsBehavior<TRequest, TResponse> : IStreamPipelineBehavior<TRequest, TResponse>
    where TRequest : IStreamRequest<TResponse>
{
    /// <inheritdoc/>
    public async IAsyncEnumerable<TResponse> Handle(
        TRequest request,
        StreamHandlerDelegate<TResponse> next,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).FullName ?? typeof(TRequest).Name;
        var startTimestamp = Stopwatch.GetTimestamp();
        var faulted = false;

        IAsyncEnumerator<TResponse> enumerator;
        try
        {
            enumerator = next().GetAsyncEnumerator(cancellationToken);
        }
        catch
        {
            faulted = true;
            var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
            ApplicationDiagnostics.RequestDuration.Record(
                elapsedMs,
                new TagList
                {
                    { "request.name", requestName },
                    { "outcome", "faulted" }
                });
            throw;
        }

        await using (enumerator.ConfigureAwait(false))
        {
            while (true)
            {
                TResponse item;
                try
                {
                    if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                        break;

                    item = enumerator.Current;
                }
                catch
                {
                    faulted = true;
                    var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
                    ApplicationDiagnostics.RequestDuration.Record(
                        elapsedMs,
                        new TagList
                        {
                            { "request.name", requestName },
                            { "outcome", "faulted" }
                        });
                    throw;
                }

                yield return item;
            }
        }

        if (!faulted)
        {
            var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
            ApplicationDiagnostics.RequestDuration.Record(
                elapsedMs,
                new TagList
                {
                    { "request.name", requestName },
                    { "outcome", "streamed" }
                });
        }
    }
}
