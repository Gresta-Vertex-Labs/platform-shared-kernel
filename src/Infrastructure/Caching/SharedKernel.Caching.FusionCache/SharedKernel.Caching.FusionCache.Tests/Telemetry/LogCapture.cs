using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace SharedKernel.Caching.FusionCache.Tests.Telemetry;

internal sealed record CapturedLog(
    string Category,
    LogLevel Level,
    EventId EventId,
    string Message,
    IReadOnlyList<KeyValuePair<string, object?>> State,
    Exception? Exception);

/// <summary>An <see cref="ILoggerProvider"/> that keeps every log entry, at every level.</summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    public ConcurrentQueue<CapturedLog> Logs { get; } = new();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, Logs);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(string category, ConcurrentQueue<CapturedLog> logs) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            logs.Enqueue(new CapturedLog(
                category,
                logLevel,
                eventId,
                formatter(state, exception),
                state as IReadOnlyList<KeyValuePair<string, object?>> ?? [],
                exception));
    }
}

internal static class Eventually
{
    /// <summary>Polls <paramref name="condition"/> until it holds or five seconds pass.</summary>
    public static async Task<bool> HoldsAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return true;
            await Task.Delay(20);
        }

        return condition();
    }
}
