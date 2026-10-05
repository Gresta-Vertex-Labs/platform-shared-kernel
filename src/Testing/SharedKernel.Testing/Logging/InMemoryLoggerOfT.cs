using Microsoft.Extensions.Logging;

namespace SharedKernel.Testing.Logging;

/// <summary>
/// A directly <c>new</c>-able <see cref="ILogger{TCategoryName}"/> test double for tests that
/// construct a handler under test by hand (no DI container) and need an
/// <see cref="ILogger{TCategoryName}"/> constructor argument.
/// </summary>
/// <remarks>
/// Implemented via composition — not inheritance — over a private <see cref="InMemoryLogger"/>,
/// mirroring <c>FakeUserContext</c>'s plain-<c>new</c>-able convention. Both this type and
/// <see cref="InMemoryLogger"/> stay <see langword="sealed"/>, per this package's standing rule
/// that fakes are leaf types with no inheritance extension point.
/// </remarks>
/// <typeparam name="TCategoryName">The type whose name is used as the logging category.</typeparam>
public sealed class InMemoryLogger<TCategoryName> : ILogger<TCategoryName>
{
    private readonly InMemoryLogger _inner = new();

    /// <summary>A snapshot of every log entry captured so far.</summary>
    public IReadOnlyList<LogRecord> Records => _inner.Records;

    /// <summary>
    /// The minimum level this logger reports as enabled via <see cref="IsEnabled(LogLevel)"/>.
    /// Defaults to <see cref="LogLevel.Trace"/>.
    /// </summary>
    public LogLevel MinLevel
    {
        get => _inner.MinLevel;
        set => _inner.MinLevel = value;
    }

    /// <inheritdoc />
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter) =>
        _inner.Log(logLevel, eventId, state, exception, formatter);

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => _inner.IsEnabled(logLevel);

    /// <inheritdoc />
    public IDisposable BeginScope<TState>(TState state)
        where TState : notnull => _inner.BeginScope(state);

    /// <summary>Empties the captured records queue — for multi-phase single-test assertions.</summary>
    public void Clear() => _inner.Clear();
}
