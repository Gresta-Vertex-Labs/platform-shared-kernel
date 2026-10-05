namespace SharedKernel.Domain.Abstractions;

/// <summary>
/// An aggregate that numbers the domain events it raises, so every event can be placed in the
/// aggregate's own history.
/// </summary>
/// <remarks>
/// <para>
/// <b>Meaning.</b> <see cref="Version"/> is the aggregate's event sequence number: <c>0</c> for an
/// aggregate that has never raised an event, increased by exactly <c>1</c> for every event it raises.
/// Right after an event is raised, it is that event's position in the aggregate's history. Clearing
/// the pending events does not change it.
/// </para>
/// <para>
/// <b>Persistence.</b> The persistence layer stores <see cref="Version"/> as a column, so an aggregate
/// loaded from the database continues numbering where it left off. A consumer that stamps each
/// published event with this number can detect a missing or out-of-order event for one aggregate.
/// </para>
/// <para>
/// <b>Pitfall.</b> It is not a concurrency token. Optimistic concurrency is
/// <see cref="IHasConcurrency.RowVersion"/>, which changes on every write, including writes that raise
/// no event.
/// </para>
/// </remarks>
public interface IHasVersion
{
    /// <summary>
    /// Gets the sequence number of the most recently raised domain event, or <c>0</c> when the
    /// aggregate has never raised one.
    /// </summary>
    int Version { get; }
}
