using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace SharedKernel.Testing.Logging;

/// <summary>
/// An in-memory <see cref="ILogger"/> test double that captures every logged entry into a
/// thread-safe queue for later assertion via <see cref="LoggerAssertions"/>.
/// </summary>
/// <remarks>
/// <para>
/// Simulates behavioral correctness only — it never enforces timing, filtering beyond
/// <see cref="MinLevel"/>, or any other production logging-provider concern.
/// </para>
/// <para>
/// <c>Logging/</c> (this namespace) is the first capability folder in
/// <c>SharedKernel.Testing</c> anchored to a cross-cutting BCL contract
/// (<c>Microsoft.Extensions.Logging.Abstractions</c>, a NuGet <c>PackageReference</c>) rather
/// than a numbered domain's own <c>.Abstractions</c> <c>ProjectReference</c>. Every other
/// capability folder in this package fakes one specific numbered domain's interface
/// (<c>ICacheService</c> → <c>02.Caching</c>, <c>IMessageBus</c> → <c>07.Messaging</c>, etc.).
/// <c>[LoggerMessage]</c>-based structured logging (root <c>CLAUDE.md</c>'s WO-041 "Logging
/// Conventions" section) has no single owning domain — it is a platform-wide authoring
/// standard consumed by every domain and owned by none — so there is no domain-specific
/// <c>.Abstractions</c> package for this folder to anchor against instead. It still obeys the
/// standing sibling-isolation rule: nothing here references <c>Caching/</c>, <c>Messaging/</c>,
/// <c>Application/</c>, or any other capability folder in this package.
/// </para>
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

        // The formatted message must be captured before any defensive copy below — the
        // caller-supplied formatter itself reads from `state`.
        var message = formatter(state, exception);

        // Some [LoggerMessage] source generators (e.g. Microsoft.Gen.Logging, pulled in
        // transitively by Microsoft.Extensions.Http.Resilience/.Telemetry) pass a pooled,
        // thread-local state object that is cleared for reuse immediately after this Log call
        // returns — an ordinary ILogger implementation is expected to have already consumed
        // everything it needs synchronously. Storing a live reference to that object would mean
        // LogRecord.State reads back empty by the time a test inspects it. A defensive
        // ToArray() copy here, taken while the state is still live, is required for correctness
        // regardless of which generator produced the caller's TState.
        var properties = state is IReadOnlyList<KeyValuePair<string, object?>> stateProperties
            ? stateProperties.ToArray()
            : null;

        var record = new LogRecord
        {
            EventId = eventId,
            LogLevel = logLevel,
            Message = message,
            State = properties,
            Exception = exception,
            Scopes = CaptureScopes(),
        };

        _records.Enqueue(record);
    }

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => logLevel >= MinLevel;

    /// <summary>
    /// Pushes <paramref name="state"/> onto an <see cref="AsyncLocal{T}"/>-backed immutable
    /// linked-list scope stack; the returned <see cref="IDisposable"/> pops exactly this node on
    /// <see cref="IDisposable.Dispose"/>.
    /// </summary>
    /// <remarks>
    /// The stack is backed by <see cref="AsyncLocal{T}"/> — not a plain field or
    /// <c>[ThreadStatic]</c> — for two reasons. First, correctness across <see langword="await"/>
    /// boundaries: a real logging provider's scope stack must still report the correct
    /// outer-to-inner chain after a nested <c>using (logger.BeginScope(...))</c> block resumes on
    /// a different thread-pool thread post-await, which <see cref="AsyncLocal{T}"/> preserves and
    /// a thread-local field would silently break. Second, isolation across parallel xUnit test
    /// collections: xUnit runs test collections concurrently by default, and a shared mutable
    /// stack (rather than one flowed per logical call context) would let one collection's scope
    /// pushes leak into another's captured <see cref="LogRecord.Scopes"/>.
    /// </remarks>
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
