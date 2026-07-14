using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace SharedKernel.Testing.Logging;

/// <summary>
/// An in-memory <see cref="ILogger"/> test double that captures every logged entry into a
/// thread-safe queue for later assertion via <see cref="LoggerAssertions"/>.
/// </summary>
/// <remarks>
/// Simulates behavioral correctness only — it never enforces timing, filtering beyond
/// <see cref="MinLevel"/>, or any other production logging-provider concern.
/// </remarks>
public sealed class InMemoryLogger : ILogger
{
    private readonly ConcurrentQueue<LogRecord> _records = new();
    private readonly AsyncLocal<ScopeNode?> _scope = new();

    /// <summary>
    /// The minimum level this logger reports as enabled via <see cref="IsEnabled(LogLevel)"/>.
    /// Defaults to <see cref="LogLevel.Trace"/> so any test that omits configuration still
    /// captures everything.
    /// </summary>
    public LogLevel MinLevel { get; set; } = LogLevel.Trace;

    /// <summary>A snapshot of every log entry captured so far.</summary>
    public IReadOnlyList<LogRecord> Records => [.. _records];

    /// <inheritdoc />
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);

        var properties = state as IReadOnlyList<KeyValuePair<string, object?>>;

        var record = new LogRecord
        {
            EventId = eventId,
            LogLevel = logLevel,
            Message = formatter(state, exception),
            State = properties,
            Exception = exception,
            Scopes = CaptureScopes(),
        };

        _records.Enqueue(record);
    }

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => logLevel >= MinLevel;

    /// <inheritdoc />
    public IDisposable BeginScope<TState>(TState state)
        where TState : notnull
    {
        var parent = _scope.Value;
        var node = new ScopeNode(state, parent);
        _scope.Value = node;
        return new ScopePopper(this, node);
    }

    /// <summary>Empties the captured records queue — for multi-phase single-test assertions.</summary>
    public void Clear() => _records.Clear();

    private IReadOnlyList<object?> CaptureScopes()
    {
        var current = _scope.Value;
        if (current is null)
        {
            return [];
        }

        var reversed = new List<object?>();
        for (var node = current; node is not null; node = node.Parent)
        {
            reversed.Add(node.State);
        }

        reversed.Reverse();
        return reversed;
    }

    private void PopScope(ScopeNode node)
    {
        // Only pop if this node is still the current top of stack (defensive against
        // out-of-order disposal); otherwise restore the parent chain from this node down.
        if (ReferenceEquals(_scope.Value, node))
        {
            _scope.Value = node.Parent;
        }
    }

    /// <summary>An immutable linked-list node in the <see cref="AsyncLocal{T}"/>-backed scope stack.</summary>
    private sealed class ScopeNode(object? state, ScopeNode? parent)
    {
        public object? State { get; } = state;
        public ScopeNode? Parent { get; } = parent;
    }

    /// <summary>Pops exactly the scope node it was created for when disposed.</summary>
    private sealed class ScopePopper(InMemoryLogger owner, ScopeNode node) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            owner.PopScope(node);
        }
    }
}
