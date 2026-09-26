using Microsoft.Extensions.Logging;

namespace SharedKernel.Application.Pipeline.Tests.Support;

/// <summary>A minimal in-memory <see cref="ILogger{TCategoryName}"/> double recording every log call.</summary>
internal sealed class FakeLogger<T> : ILogger<T>
{
    public List<FakeLogEntry> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NoopScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Entries.Add(new FakeLogEntry(logLevel, eventId, formatter(state, exception), exception));
    }

    private sealed class NoopScope : IDisposable
    {
        public static readonly NoopScope Instance = new();
        public void Dispose() { }
    }
}

internal sealed record FakeLogEntry(LogLevel Level, EventId EventId, string Message, Exception? Exception);
