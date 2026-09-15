namespace SharedKernel.Domain.Abstractions;

/// <summary>
/// An aggregate that numbers the domain events it raises, so every event can be placed in the
/// aggregate's own history.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Version"/> is the <b>event sequence number</b> of the aggregate: <c>0</c> for an
/// aggregate that has never raised an event, and increased by exactly <c>1</c> for every event it
/// raises. After raising an event, <see cref="Version"/> is that event's position in the
/// aggregate's history. Clearing the pending events does not change it.
/// </para>
/// <para>
/// <b>It is only meaningful when persisted.</b> The persistence layer maps <see cref="Version"/>
/// as a column, so an aggregate loaded from the database continues numbering where it left off.
/// Consumers can then stamp each published event with the aggregate's version and detect a
/// missing or out-of-order event for one aggregate, which a concurrency token cannot express.
/// </para>
/// <para>
/// It is not a concurrency token. Optimistic concurrency is <see cref="IHasConcurrency.RowVersion"/>,
/// which changes on every write, including writes that raise no event.
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
