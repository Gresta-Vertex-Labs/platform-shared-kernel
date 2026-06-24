using Bogus;
using SharedKernel.Domain.Aggregates;

namespace SharedKernel.Testing.Persistence;

/// <summary>
/// Abstract <see cref="Faker{T}"/> base for generating <see cref="AggregateRoot{TId}"/>-derived
/// test instances with EF Core interceptor expectations pre-satisfied.
/// </summary>
/// <typeparam name="TAggregate">The aggregate root type being faked.</typeparam>
/// <typeparam name="TId">The aggregate's identity key type.</typeparam>
/// <remarks>
/// Pre-configures audit-shaped expectations (a concrete faker subclass is expected to populate
/// <c>CreatedBy</c>/<c>CreatedOn</c>/<c>IsDeleted = false</c> via its own <c>RuleFor</c> calls,
/// matching what the platform's <c>AuditInterceptor</c>/<c>SoftDeleteInterceptor</c> would set on
/// a real save) — this base does not set those values itself since audit properties have
/// <c>private set</c> and are populated only by EF Core interceptors at save time.
/// </remarks>
public abstract class AggregateRootFaker<TAggregate, TId> : Faker<TAggregate>
    where TAggregate : AggregateRoot<TId>
    where TId : notnull;
