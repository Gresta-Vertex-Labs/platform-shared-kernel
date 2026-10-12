using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SharedKernel.Execution.Context;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Testing.Logging;

namespace SharedKernel.Scheduling.Tests.TestSupport;

/// <summary>
/// One log call together with the ambient <see cref="IRequestContext"/> that was current on the calling
/// thread when it was made (the shared <see cref="InMemoryLogger"/> does not capture ambient state).
/// </summary>
internal sealed record AmbientLogRecord(
    EventId EventId,
    LogLevel Level,
    string Category,
    bool HasContext,
    string? Identity,
    string? CorrelationId,
    TenantId? TenantId);

/// <summary>
/// An <see cref="ILoggerFactory"/> that delegates to an <see cref="InMemoryLoggerFactory"/> and snapshots
/// <see cref="RequestContextScope.Current"/> for every record, in call order, across all categories.
/// </summary>
internal sealed class AmbientContextLoggerFactory : ILoggerFactory
{
    private readonly ConcurrentQueue<AmbientLogRecord> _ambient = new();

    public InMemoryLoggerFactory Inner { get; } = new();

    public IReadOnlyList<AmbientLogRecord> Ambient => [.. _ambient];

    /// <summary>Every record of every category, as the shared in-memory logger captured it.</summary>
    public IReadOnlyList<LogRecord> AllRecords => [.. Inner.Loggers.Values.SelectMany(l => l.Records)];

    public ILogger CreateLogger(string categoryName) => new AmbientLogger(Inner.GetLogger(categoryName), categoryName, _ambient);

    public void AddProvider(ILoggerProvider provider) => Inner.AddProvider(provider);

    public void Dispose() => Inner.Dispose();

    private sealed class AmbientLogger(InMemoryLogger inner, string category, ConcurrentQueue<AmbientLogRecord> sink) : ILogger
    {
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            IRequestContext? current = RequestContextScope.Current;
            sink.Enqueue(new AmbientLogRecord(
                eventId,
                logLevel,
                category,
                current is not null,
                current?.UserId,
                current?.CorrelationId,
                current?.TenantId));

            inner.Log(logLevel, eventId, state, exception, formatter);
        }

        public bool IsEnabled(LogLevel logLevel) => inner.IsEnabled(logLevel);

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => inner.BeginScope(state);
    }
}
