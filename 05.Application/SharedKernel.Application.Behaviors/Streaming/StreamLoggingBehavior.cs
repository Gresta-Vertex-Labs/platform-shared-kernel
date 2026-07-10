using MediatR;
using Microsoft.Extensions.Logging;
using SharedKernel.Application.Behaviors.Shared;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace SharedKernel.Application.Behaviors.Streaming;

/// <summary>
/// Logs the lifecycle of a streaming request: entry, first-item latency, completion, and fault.
/// </summary>
/// <typeparam name="TRequest">The streaming request type.</typeparam>
/// <typeparam name="TResponse">The per-item payload type yielded by the stream.</typeparam>
/// <remarks>
/// <list type="bullet">
///   <item><description><c>Information</c> — stream opened (entry).</description></item>
///   <item>
///     <description>
///       <c>Debug</c> — first item yielded (first-item latency from stream open); only logged
///       when at least one item is produced.
///     </description>
///   </item>
///   <item><description><c>Information</c> — stream completed (all items produced).</description></item>
///   <item>
///     <description>
///       <c>Warning</c> — stream faulted (exception thrown during enumeration); the exception
///       propagates unchanged — never swallowed.
///     </description>
///   </item>
/// </list>
/// Never logs request or item payloads — only timing and type information.
/// </remarks>
public sealed partial class StreamLoggingBehavior<TRequest, TResponse>(ILogger<TRequest> logger)
    : IStreamPipelineBehavior<TRequest, TResponse>
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
        var firstItemLogged = false;

        LogStreamStarted(logger, requestName);

        IAsyncEnumerator<TResponse> enumerator;
        try
        {
            enumerator = next().GetAsyncEnumerator(cancellationToken);
        }
        catch (Exception ex)
        {
            var elapsed = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
            LogStreamFaulted(logger, requestName, elapsed, ex);
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
                catch (Exception ex)
                {
                    var elapsed = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
                    LogStreamFaulted(logger, requestName, elapsed, ex);
                    throw;
                }

                if (!firstItemLogged)
                {
                    firstItemLogged = true;
                    var firstItemElapsed = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
                    LogFirstItem(logger, requestName, firstItemElapsed);
                }

                yield return item;
            }
        }

        var completionElapsed = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
        LogStreamCompleted(logger, requestName, completionElapsed);
    }

    /// <summary>Stream-opened entry log (EventId 5120, Information) — emitted once before enumeration begins.</summary>
    [LoggerMessage(
        EventId = ApplicationBehaviorsLoggingEventIds.LogStreamStarted,
        Level = LogLevel.Information,
        Message = "Streaming {RequestName} started.")]
    private static partial void LogStreamStarted(ILogger logger, string requestName);

    /// <summary>First-item latency log (EventId 5121, Debug) — emitted once, the first time <c>MoveNextAsync</c> yields an item.</summary>
    [LoggerMessage(
        EventId = ApplicationBehaviorsLoggingEventIds.LogFirstItem,
        Level = LogLevel.Debug,
        Message = "Streaming {RequestName} produced first item in {ElapsedMilliseconds}ms.")]
    private static partial void LogFirstItem(ILogger logger, string requestName, double elapsedMilliseconds);

    /// <summary>Stream-completed log (EventId 5122, Information) — emitted once enumeration finishes normally.</summary>
    [LoggerMessage(
        EventId = ApplicationBehaviorsLoggingEventIds.LogStreamCompleted,
        Level = LogLevel.Information,
        Message = "Streaming {RequestName} completed in {ElapsedMilliseconds}ms.")]
    private static partial void LogStreamCompleted(ILogger logger, string requestName, double elapsedMilliseconds);

    /// <summary>Stream-faulted log (EventId 5123, Warning) — emitted when enumeration throws; the exception is rethrown unchanged after this log call.</summary>
    [LoggerMessage(
        EventId = ApplicationBehaviorsLoggingEventIds.LogStreamFaulted,
        Level = LogLevel.Warning,
        Message = "Streaming {RequestName} faulted after {ElapsedMilliseconds}ms.")]
    private static partial void LogStreamFaulted(ILogger logger, string requestName, double elapsedMilliseconds, Exception exception);
}
