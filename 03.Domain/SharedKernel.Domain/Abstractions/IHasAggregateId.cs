namespace SharedKernel.Domain.Abstractions;

/// <summary>
/// Opt-in marker interface correlating a domain event back to the aggregate that raised it.
/// </summary>
/// <typeparam name="TId">The type of the raising aggregate's identity key. Must be non-null.</typeparam>
/// <remarks>
/// <para>
/// This is a zero-ceremony shape deliberately mirroring <see cref="IHasTenant"/> exactly — a
/// single-property marker with no domain logic. <see cref="SharedKernel.Domain.Events.IDomainEvent"/>,
/// <see cref="SharedKernel.Domain.Events.DomainEvent"/>, and
/// <see cref="SharedKernel.Domain.Events.DomainEvent{TPayload}"/> are completely unchanged — this
/// interface is purely something a concrete event may additionally implement, never a new required
/// member on the existing event hierarchy.
/// </para>
/// <para>
/// Gives infrastructure code (audit trails, outbox/messaging translation, projections, logging) a
/// single <c>is IHasAggregateId&lt;TId&gt;</c> type check instead of per-event ad hoc
/// property-name conventions (<c>OrderId</c>, <c>SourceId</c>, etc.).
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public sealed record OrderPlacedPayload(Guid OrderId, decimal Total);
///
/// public sealed record OrderPlacedEvent(OrderId AggregateId)
///     : DomainEvent&lt;OrderPlacedPayload&gt;, IHasAggregateId&lt;OrderId&gt;;
///
/// // Infrastructure code can now correlate any event back to its raising aggregate uniformly:
/// if (domainEvent is IHasAggregateId&lt;OrderId&gt; correlated)
/// {
///     var aggregateId = correlated.AggregateId;
/// }
/// </code>
/// </example>
public interface IHasAggregateId<TId>
    where TId : notnull
{
    /// <summary>Gets the identity key of the aggregate that raised this event.</summary>
    TId AggregateId { get; }
}
