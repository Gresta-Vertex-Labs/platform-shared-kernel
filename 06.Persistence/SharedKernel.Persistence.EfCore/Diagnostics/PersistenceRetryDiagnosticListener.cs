using System.Diagnostics;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SharedKernel.Persistence.EfCore.Diagnostics;

/// <summary>
/// Hosted service that subscribes to EF Core's own provider-neutral
/// <see cref="CoreEventId.ExecutionStrategyRetrying"/> diagnostic event and logs a
/// <c>TransientRetryAttempt</c> Warning (EventId <c>6007</c>) for each observed retry
/// (WO-053/P-333).
/// </summary>
/// <remarks>
/// <para>
/// Registered by <c>EfCorePersistenceBuilder{TContext}.Build()</c> only when
/// <c>.WithTransientFaultRetry()</c> was called. Subscribes against
/// <see cref="System.Diagnostics.DiagnosticListener.AllListeners"/>, filtering for the listener
/// named <c>"Microsoft.EntityFrameworkCore"</c> — the core EF Core assembly's own diagnostic
/// source, never Npgsql's — preserving the platform's "EfCore never references Npgsql" hard rule.
/// </para>
/// <para>
/// Registered as an <see cref="IHostedService"/> specifically so the
/// <see cref="System.Diagnostics.DiagnosticListener.AllListeners"/> subscription is active for the
/// app's entire lifetime, started once at host startup and torn down at host shutdown.
/// </para>
/// </remarks>
internal sealed class PersistenceRetryDiagnosticListener
    : IHostedService, IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>
{
    private const string EfCoreDiagnosticListenerName = "Microsoft.EntityFrameworkCore";

    private readonly ILogger<PersistenceRetryDiagnosticListener> _logger;
    private readonly List<IDisposable> _sourceSubscriptions = [];
    private IDisposable? _allListenersSubscription;

    /// <summary>Initialises a new <see cref="PersistenceRetryDiagnosticListener"/>.</summary>
    /// <param name="logger">
    /// Optional logger for the <c>TransientRetryAttempt</c> Warning. Resolved by DI when
    /// registered; falls back to <see cref="NullLogger{T}"/> otherwise.
    /// </param>
    public PersistenceRetryDiagnosticListener(ILogger<PersistenceRetryDiagnosticListener>? logger = null)
    {
        _logger = logger ?? NullLogger<PersistenceRetryDiagnosticListener>.Instance;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _allListenersSubscription = DiagnosticListener.AllListeners.Subscribe(this);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _allListenersSubscription?.Dispose();
        _allListenersSubscription = null;

        foreach (var subscription in _sourceSubscriptions)
            subscription.Dispose();

        _sourceSubscriptions.Clear();

        return Task.CompletedTask;
    }

    void IObserver<DiagnosticListener>.OnNext(DiagnosticListener listener)
    {
        if (listener.Name != EfCoreDiagnosticListenerName)
            return;

        var subscription = listener.Subscribe(
            this,
            eventName => eventName == CoreEventId.ExecutionStrategyRetrying.Name);
        _sourceSubscriptions.Add(subscription);
    }

    void IObserver<DiagnosticListener>.OnCompleted() { }

    void IObserver<DiagnosticListener>.OnError(Exception error) { }

    void IObserver<KeyValuePair<string, object?>>.OnNext(KeyValuePair<string, object?> value)
    {
        if (value.Value is not ExecutionStrategyEventData eventData)
            return;

        var attemptNumber = eventData.ExceptionsEncountered.Count;
        PersistenceLog.TransientRetryAttempt(_logger, attemptNumber);
    }

    void IObserver<KeyValuePair<string, object?>>.OnCompleted() { }

    void IObserver<KeyValuePair<string, object?>>.OnError(Exception error) { }
}
