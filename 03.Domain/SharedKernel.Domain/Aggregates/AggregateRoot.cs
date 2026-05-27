using SharedKernel.Core.Exceptions;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.BusinessRules;
using SharedKernel.Domain.Entities;
using SharedKernel.Domain.Events;
using SharedKernel.Domain.Exceptions;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Domain.Aggregates;

/// <summary>
/// Abstract base class for all DDD aggregate roots.
/// Extends <see cref="Entity{TId}"/> with domain event accumulation, clock-sourced event
/// factories, and business rule enforcement.
/// </summary>
/// <typeparam name="TId">The type of the aggregate's identity key. Must be non-null.</typeparam>
/// <remarks>
/// <para>
/// Inject an <see cref="IClock"/> via the primary constructor so that domain events carry
/// accurate timestamps. The protected parameterless constructor assigns <see cref="NullClock"/>
/// for ORM materialisation paths — no events should be raised during hydration.
/// </para>
/// <para>
/// Only this class and its subclasses may call <see cref="RaiseDomainEvent(IDomainEvent)"/>.
/// Plain entities must not accumulate domain events.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public sealed record OrderId(Guid Value) : StronglyTypedId&lt;Guid&gt;(Value);
///
/// public sealed class Order : AggregateRoot&lt;OrderId&gt;
/// {
///     public string CustomerName { get; private set; }
///
///     public Order(OrderId id, string customerName, IClock clock) : base(id, clock)
///     {
///         CustomerName = customerName;
///         RaiseDomainEvent(ts =&gt; new OrderCreatedEvent(id.Value) { OccurredOn = ts });
///     }
///
///     protected Order() { } // ORM path
/// }
/// </code>
/// </example>
public abstract class AggregateRoot<TId> : Entity<TId>, IAggregateRoot<TId>, IHasVersion where TId : notnull
{
    private readonly List<IDomainEvent> _domainEvents = [];
    private IClock _clock;

    /// <summary>
    /// Initialises a new aggregate root with the specified identity key and clock.
    /// </summary>
    /// <param name="id">The aggregate's identity key.</param>
    /// <param name="clock">The clock used to timestamp domain events raised by this aggregate.</param>
    protected AggregateRoot(TId id, IClock clock) : base(id) => _clock = clock;

    /// <summary>
    /// Protected parameterless constructor for ORM materialisation paths (e.g., EF Core proxies).
    /// Assigns <see cref="NullClock"/> so <c>_clock</c> is never null.
    /// Do not call directly in domain code.
    /// </summary>
    protected AggregateRoot() : base() => _clock = NullClock.Instance;

    /// <inheritdoc/>
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    /// <inheritdoc/>
    public int Version { get; private set; }

    /// <inheritdoc/>
    /// <remarks>
    /// This method is intended exclusively for infrastructure dispatch code (e.g., an EF Core interceptor
    /// or outbox publisher) after domain events have been successfully committed and dispatched.
    /// Aggregates must never call <c>ClearDomainEvents()</c> on themselves — doing so would silently
    /// discard events before infrastructure has had a chance to process them.
    /// </remarks>
    public void ClearDomainEvents() => _domainEvents.Clear();

    /// <summary>
    /// Gets the current UTC time from the injected clock.
    /// Subclasses can use this to timestamp operations without exposing the full <see cref="IClock"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Do not use <see cref="Now"/> to supply <c>OccurredOn</c> for domain events.</strong>
    /// Although it compiles, writing <c>new MyEvent { OccurredOn = Now }</c> is a soft violation
    /// of the deterministic timestamp contract: the timestamp is captured at object-construction time
    /// rather than at event-registration time, which makes event timestamps non-deterministic in unit
    /// tests that control the clock via a fake <see cref="IClock"/>.
    /// </para>
    /// <para>
    /// Use the factory overload instead:
    /// <code>
    /// RaiseDomainEvent(ts => new MyEvent { OccurredOn = ts });
    /// </code>
    /// The <c>RaiseDomainEvent(Func&lt;DateTimeOffset, IDomainEvent&gt;)</c> overload passes
    /// <c>_clock.UtcNow</c> to the factory exactly when the event is registered, ensuring that
    /// the timestamp is always sourced from the injected <see cref="IClock"/>. This allows tests
    /// to advance a fake clock to a known point and then assert that the raised event carries
    /// exactly that timestamp.
    /// </para>
    /// </remarks>
    protected DateTimeOffset Now => _clock.UtcNow;

    /// <summary>
    /// Adds a pre-constructed <paramref name="domainEvent"/> to this aggregate's event collection
    /// and increments <see cref="Version"/>.
    /// </summary>
    /// <param name="domainEvent">The domain event to record.</param>
    protected void RaiseDomainEvent(IDomainEvent domainEvent)
    {
        _domainEvents.Add(domainEvent);
        Version++;
    }

    /// <summary>
    /// Adds a domain event constructed via <paramref name="factory"/>, passing the current UTC
    /// timestamp sourced from the injected <see cref="IClock"/>. Use this overload to ensure
    /// events carry accurate, deterministic timestamps in tests.
    /// Increments <see cref="Version"/>.
    /// </summary>
    /// <param name="factory">
    /// A delegate that accepts the current <see cref="DateTimeOffset"/> and returns the domain event.
    /// </param>
    protected void RaiseDomainEvent(Func<DateTimeOffset, IDomainEvent> factory)
    {
        _domainEvents.Add(factory(_clock.UtcNow));
        Version++;
    }

    /// <summary>
    /// Evaluates <paramref name="rule"/> and throws <see cref="BusinessRuleViolationException"/>
    /// if the rule is broken.
    /// </summary>
    /// <param name="rule">The business rule to enforce.</param>
    /// <exception cref="BusinessRuleViolationException">Thrown when <paramref name="rule"/> is broken.</exception>
    protected static void CheckRule(IBusinessRule rule)
    {
        if (rule.IsBroken())
            throw new BusinessRuleViolationException(rule);
    }

    /// <summary>
    /// Executes the <paramref name="factory"/> and wraps the result in a railway-friendly
    /// <see cref="Result{T}"/>. Exceptions raised during construction are converted to
    /// <see cref="Result{T}.Failure"/> rather than propagating.
    /// </summary>
    /// <typeparam name="T">The type of the aggregate or object produced by the factory.</typeparam>
    /// <param name="factory">The construction delegate. Typically a lambda that calls <c>new AggregateType(...)</c>.</param>
    /// <returns>
    /// <see cref="Result{T}.Success"/> when the factory completes without throwing.
    /// <see cref="Result{T}.Failure"/> with <c>ErrorType.BusinessRule</c> when a
    /// <see cref="BusinessRuleViolationException"/> is thrown.
    /// <see cref="Result{T}.Failure"/> with the first validation error when a
    /// <see cref="ValidationException"/> is thrown.
    /// </returns>
    /// <example>
    /// <code>
    /// public static Result&lt;Order&gt; Create(Guid id, string customerName, IClock clock)
    ///     => TryCreate(() => new Order(new OrderId(id), customerName, clock));
    /// </code>
    /// </example>
    protected static Result<T> TryCreate<T>(Func<T> factory)
    {
        try
        {
            return Result<T>.Success(factory());
        }
        catch (BusinessRuleViolationException ex)
        {
            return Result<T>.Failure(ex.Error);
        }
        catch (ValidationException ex)
        {
            return Result<T>.Failure(ex.Errors[0]);
        }
    }
}
