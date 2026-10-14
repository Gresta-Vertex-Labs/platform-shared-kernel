using Microsoft.Extensions.Logging;

namespace SharedKernel.Storage.S3.Tests.Infrastructure;

/// <summary>Collects every log record as one string (rendered text plus each structured state value) for leak scans.</summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly object _gate = new();
    private readonly List<string> _records = [];

    /// <summary>A snapshot of the captured records.</summary>
    public IReadOnlyList<string> Records
    {
        get
        {
            lock (_gate)
            {
                return [.. _records];
            }
        }
    }

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this);

    public void Dispose()
    {
    }

    private void Add(string record)
    {
        lock (_gate)
        {
            _records.Add(record);
        }
    }

    private sealed class CapturingLogger(CapturingLoggerProvider owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            string state0 = state is IEnumerable<KeyValuePair<string, object?>> pairs
                ? string.Join(' ', pairs.Select(p => $"{p.Key}={p.Value}"))
                : string.Empty;
            owner.Add($"{formatter(state, exception)} {state0}");
        }
    }
}
