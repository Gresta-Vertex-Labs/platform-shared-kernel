using SharedKernel.Domain.Aggregates;

namespace SharedKernel.Testing.Persistence;

/// <summary>
/// Abstract <see cref="AggregateRootFaker{TAggregate, TId}"/> base for tenant-scoped aggregates,
/// additionally expecting a non-empty <c>TenantId</c> on every generated instance.
/// </summary>
/// <typeparam name="TAggregate">The tenanted aggregate root type being faked.</typeparam>
/// <typeparam name="TId">The aggregate's identity key type.</typeparam>
/// <remarks>
/// <c>TenantId</c> on tenanted aggregate bases is construction-time only (<c>private set</c>) —
/// concrete faker subclasses populate it by passing a non-empty <see cref="Guid"/> to the
/// aggregate's constructor inside their own <c>CustomInstantiator</c>/<c>RuleFor</c> declarations.
/// </remarks>
public abstract class TenantedAggregateFaker<TAggregate, TId> : AggregateRootFaker<TAggregate, TId>
    where TAggregate : AggregateRoot<TId>
    where TId : notnull;
