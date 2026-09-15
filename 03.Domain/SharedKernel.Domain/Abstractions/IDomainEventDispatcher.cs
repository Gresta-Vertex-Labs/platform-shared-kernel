using SharedKernel.Domain.Events;

namespace SharedKernel.Domain.Abstractions;

/// <summary>
/// Defines the contract for dispatching a batch of domain events raised by one or more aggregate roots.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Empty list is a no-op:</strong> Any conforming implementation must treat an empty
/// <c>events</c> list as a no-op and return immediately without performing any work.
/// </para>
/// <para>
/// <strong>Exceptions propagate unchanged:</strong> If a handler throws, the exception must propagate
/// to the caller without being swallowed, wrapped, or logged-and-suppressed. Callers are responsible
/// for deciding how to handle handler failures.
/// </para>
/// <para>
/// <strong>Opt-in DI contract:</strong> This interface is not auto-registered by any infrastructure or
/// persistence builder. Consuming services must explicitly register an implementation alongside their
/// persistence or infrastructure configuration (e.g., next to the EF Core persistence builder). The
/// <c>06.Persistence.EfCore</c> layer resolves this interface as an optional dependency — if no
/// implementation is registered, event dispatch is silently skipped.
/// </para>
/// <para>
/// The MediatR-based implementation (<c>MediatRDomainEventDispatcher</c>) belongs in
/// <c>05.Application</c> and is not shipped by this package.
/// </para>
/// </remarks>
public interface IDomainEventDispatcher
{
    /// <summary>
    /// Dispatches a collection of domain events to their respective handlers.
    /// </summary>
    /// <param name="events">
    /// The ordered list of domain events to dispatch. Must not be <see langword="null"/>.
    /// An empty list is treated as a no-op.
    /// </param>
    /// <param name="cancellationToken">
    /// A cancellation token that can be used to cancel the dispatch operation.
    /// </param>
    /// <returns>
    /// A <see cref="Task"/> that represents the asynchronous dispatch operation. The task completes
    /// when all events have been dispatched (or immediately when <paramref name="events"/> is empty).
    /// </returns>
    Task DispatchAsync(IReadOnlyList<IDomainEvent> events, CancellationToken cancellationToken);
}
