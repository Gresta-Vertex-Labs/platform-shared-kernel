namespace SharedKernel.Application.DomainEvents;

/// <summary>
/// Configuration options for <see cref="MediatRDomainEventDispatcher"/>.
/// </summary>
/// <remarks>
/// Configure via <c>services.AddSharedKernelApplication(opts => opts.ParallelDispatch = true)</c>
/// at the composition root. The parameterless overload retains the serial default and never breaks
/// existing call sites.
/// </remarks>
public sealed class MediatRDomainEventDispatcherOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether domain events are dispatched concurrently.
    /// </summary>
    /// <value>
    /// <see langword="false"/> (default) — events are published serially in list order, each
    /// <c>IPublisher.Publish</c> awaited before the next begins. Serial is the safe default:
    /// events sharing an ordering dependency are correctly sequenced.
    /// <br/>
    /// <see langword="true"/> — all events in a single <c>DispatchAsync</c> call are dispatched
    /// concurrently via <c>Task.WhenAll</c>. All events are dispatched even if early ones fault —
    /// exceptions are collected and rethrown as an <see cref="AggregateException"/> after all
    /// dispatches complete.
    /// </value>
    /// <remarks>
    /// <b>Documented constraint:</b> parallel dispatch is only valid for independently-observable
    /// events with no ordering dependency between them. Opting in with ordered events is a
    /// documented misuse — not mechanically prevented (no ordering declaration on
    /// <c>IDomainEvent</c>), so code review must catch it.
    /// </remarks>
    public bool ParallelDispatch { get; set; }
}
