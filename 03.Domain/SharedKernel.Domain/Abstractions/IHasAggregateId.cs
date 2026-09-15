namespace SharedKernel.Domain.Abstractions;

/// <summary>
/// An optional contract for a domain event that carries the identity key of the aggregate that
/// raised it.
/// </summary>
/// <typeparam name="TId">The identity key type of the raising aggregate. Must be non-null.</typeparam>
/// <remarks>
/// <para>
/// <b>Usage.</b> Implement it on a concrete event when infrastructure, such as an audit trail, outbox
/// translation or a projection, must correlate the event with its aggregate. That code then needs one
/// type check, <c>is IHasAggregateId&lt;TId&gt;</c>, instead of knowing each event's own property name.
/// </para>
/// <para>
/// It is opt-in: <see cref="SharedKernel.Domain.Events.IDomainEvent"/> does not require it.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [DomainEventVersion(1)]
/// public sealed record OrderPlaced(OrderId AggregateId, Money Total) : DomainEvent, IHasAggregateId&lt;OrderId&gt;;
///
/// // In infrastructure:
/// if (domainEvent is IHasAggregateId&lt;OrderId&gt; correlated)
/// {
///     OrderId orderId = correlated.AggregateId;
/// }
/// </code>
/// </example>
public interface IHasAggregateId<TId>
    where TId : notnull
{
    /// <summary>Gets the identity key of the aggregate that raised the event.</summary>
    TId AggregateId { get; }
}
