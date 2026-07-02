using MediatR;
using Microsoft.Extensions.Logging;
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
public sealed class StreamLoggingBehavior<TRequest, TResponse>(ILogger<TRequest> logger)
    : IStreamPipelineBehavior<TRequest, TResponse>
    where TRequest : IStreamRequest<TResponse>
{
    private static readonly Action<ILogger, string, Exception?> LogStreamStarted =
        LoggerMessage.Define<string>(LogLevel.Information, new EventId(1, "StreamStarted"),
            "Streaming {RequestName} started.");

    private static readonly Action<ILogger, string, double, Exception?> LogFirstItem =
        LoggerMessage.Define<string, double>(LogLevel.Debug, new EventId(2, "StreamFirstItem"),
            "Streaming {RequestName} produced first item in {ElapsedMilliseconds}ms.");

    private static readonly Action<ILogger, string, double, Exception?> LogStreamCompleted =
        LoggerMessage.Define<string, double>(LogLevel.Information, new EventId(3, "StreamCompleted"),
            "Streaming {RequestName} completed in {ElapsedMilliseconds}ms.");

    private static readonly Action<ILogger, string, double, Exception?> LogStreamFaulted =
        LoggerMessage.Define<string, double>(LogLevel.Warning, new EventId(4, "StreamFaulted"),
            "Streaming {RequestName} faulted after {ElapsedMilliseconds}ms.");

    /// <inheritdoc/>
    public async IAsyncEnumerable<TResponse> Handle(
        TRequest request,
        StreamHandlerDelegate<TResponse> next,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).FullName ?? typeof(TRequest).Name;
        var startTimestamp = Stopwatch.GetTimestamp();
        var firstItemLogged = false;

        LogStreamStarted(logger, requestName, null);

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
                    LogFirstItem(logger, requestName, firstItemElapsed, null);
                }

                yield return item;
            }
        }

        var completionElapsed = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
        LogStreamCompleted(logger, requestName, completionElapsed, null);
    }
}
