using Microsoft.Extensions.Logging;
using SharedKernel.Caching.Redis.PubSub.Diagnostics;
using StackExchange.Redis;

namespace SharedKernel.Caching.Redis.PubSub;

/// <summary>
/// One subscription: reads its queue and calls the handler for each message, one at a time, until disposed.
/// </summary>
internal sealed class RedisChannelSubscription : IAsyncDisposable
{
    private readonly string _channel;
    private readonly ChannelMessageQueue _queue;
    private readonly Func<string, CancellationToken, ValueTask> _handler;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _stopping = new();

    // True while this subscription's handler runs, so disposing it from inside that handler does not wait for
    // itself. One per subscription: disposing another subscription from a handler still waits for that one.
    private readonly AsyncLocal<bool> _inHandler = new();

    private Task _loop = Task.CompletedTask;
    private int _disposed;

    private RedisChannelSubscription(
        string channel,
        ChannelMessageQueue queue,
        Func<string, CancellationToken, ValueTask> handler,
        ILogger logger)
    {
        _channel = channel;
        _queue = queue;
        _handler = handler;
        _logger = logger;
    }

    internal static RedisChannelSubscription Start(
        string channel,
        ChannelMessageQueue queue,
        Func<string, CancellationToken, ValueTask> handler,
        ILogger logger)
    {
        var subscription = new RedisChannelSubscription(channel, queue, handler, logger);
        subscription._loop = Task.Run(subscription.ReadAsync);
        return subscription;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;

        try
        {
            // Completes the queue, which ends the read loop after the current message.
            await _queue.UnsubscribeAsync().ConfigureAwait(false);
        }
        // StackExchange.Redis does not always wrap a dropped connection in a RedisException: a completed socket pipe
        // or a disposed multiplexer surfaces as InvalidOperationException, a transport error as IOException.
        catch (Exception ex) when (ex is RedisException or TimeoutException or InvalidOperationException or IOException)
        {
            // The server forgets the subscription when the connection drops; nothing is left to clean up.
            PubSubLog.UnsubscribeFailed(_logger, _channel, ex);
        }

        try
        {
            await _stopping.CancelAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // The read loop already ended and disposed it.
        }

        // The read loop owns and disposes the token source. From inside the handler it ends once the handler returns.
        if (!_inHandler.Value)
            await _loop.ConfigureAwait(false);
    }

    private async Task ReadAsync()
    {
        var token = _stopping.Token;
        try
        {
            await foreach (var message in _queue.WithCancellation(token).ConfigureAwait(false))
            {
                _inHandler.Value = true;
                try
                {
                    await _handler(message.Message.ToString(), token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    PubSubLog.HandlerFailed(_logger, _channel, ex);
                }
                finally
                {
                    _inHandler.Value = false;
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Disposed.
        }
        finally
        {
            _stopping.Dispose();
        }
    }
}
