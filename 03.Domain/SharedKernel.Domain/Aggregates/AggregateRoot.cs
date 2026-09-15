using SharedKernel.Core.Exceptions;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.BusinessRules;
using SharedKernel.Domain.Entities;
using SharedKernel.Domain.Events;
using SharedKernel.Domain.Exceptions;
using SharedKernel.Domain.Internal;
using SharedKernel.Guards;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Domain.Aggregates;

/// <summary>
/// Base class for aggregate roots: an entity that guards its own invariants, records the domain
/// events it raises, and reads time from an injected <see cref="IClock"/>.
/// </summary>
/// <typeparam name="TId">The identity key type. Must be non-null.</typeparam>
/// <remarks>
/// <para>
/// <b>Time.</b> Construct with an <see cref="IClock"/>. An aggregate materialized by an ORM through the
/// parameterless constructor has no clock until infrastructure calls <see cref="IHasClock.AttachClock"/>;
/// reading <see cref="Now"/> before then throws <see cref="InvalidOperationException"/>, so a missing
/// clock can never become a year-0001 timestamp in an event or a soft-delete column.
/// </para>
/// <para>
/// <b>Events.</b> Raise events with <see cref="RaiseDomainEvent(Func{DateTimeOffset, IDomainEvent})"/>
/// so the timestamp comes from the clock at the moment the event is recorded. Each raised event
/// advances <see cref="Version"/> by one. Infrastructure dispatches the pending events after the unit
/// of work commits and then calls <see cref="ClearDomainEvents"/>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public sealed class Order : AggregateRoot&lt;OrderId&gt;
/// {
///     private Order(OrderId id, string customerName, IClock clock) : base(id, clock)
///     {
///         Guard.Throw.NullOrWhiteSpace(customerName);
///         CustomerName = customerName;
///         RaiseDomainEvent(at =&gt; new OrderPlaced(id.Value) { OccurredOn = at });
///     }
///
///     private Order() { } // ORM
///
///     public string CustomerName { get; private set; } = string.Empty;
///
///     public static ValidationResult&lt;Order&gt; Create(OrderId id, string customerName, IClock clock) =&gt;
///         TryCreate(() =&gt; new Order(id, customerName, clock));
/// }
/// </code>
/// </example>
public abstract class AggregateRoot<TId> : Entity<TId>, IAggregateRoot<TId>, IHasVersion, IHasClock
    where TId : notnull
{
    private readonly List<IDomainEvent> _domainEvents = [];
    private IClock? _clock;

    /// <summary>Initializes an aggregate root with its identity key and clock.</summary>
    /// <param name="id">The identity key.</param>
    /// <param name="clock">The clock that timestamps events and time-dependent state.</param>
    /// <exception cref="DomainException"><paramref name="clock"/> is <see langword="null"/>.</exception>
    protected AggregateRoot(TId id, IClock clock) : base(id)
    {
        Guard.Throw.Null(clock);
        _clock = clock;
    }

    /// <summary>
    /// Initializes an aggregate root for ORM materialization, without a clock. Do not call from
    /// domain code.
    /// </summary>
    protected AggregateRoot() { }

    /// <inheritdoc/>
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    /// <inheritdoc/>
    public int Version { get; private set; }

    /// <inheritdoc/>
    bool IHasClock.IsClockAttached => _clock is not null;

    /// <summary>Gets the current UTC time from the aggregate's clock.</summary>
    /// <remarks>
    /// Use it for time-dependent state such as due dates. For an event timestamp use
    /// <see cref="RaiseDomainEvent(Func{DateTimeOffset, IDomainEvent})"/>, which reads the clock
    /// at the moment the event is recorded.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// No clock is attached: the aggregate was materialized by an ORM and infrastructure has not
    /// called <see cref="IHasClock.AttachClock"/>.
    /// </exception>
    protected DateTimeOffset Now =>
        (_clock ?? throw new InvalidOperationException(
            $"{GetType().Name} has no clock. It was created through its parameterless constructor, "
            + $"and infrastructure must call {nameof(IHasClock)}.{nameof(IHasClock.AttachClock)} "
            + "before the aggregate reads the time.")).UtcNow;

    /// <inheritdoc/>
    void IHasClock.AttachClock(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        _clock = clock;
    }

    /// <inheritdoc/>
    public void ClearDomainEvents() => _domainEvents.Clear();

    /// <summary>Records <paramref name="domainEvent"/> and advances <see cref="Version"/> by one.</summary>
    /// <param name="domainEvent">The event to record.</param>
    /// <exception cref="ArgumentNullException"><paramref name="domainEvent"/> is <see langword="null"/>.</exception>
    protected void RaiseDomainEvent(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        _domainEvents.Add(domainEvent);
        Version++;
    }

    /// <summary>
    /// Creates an event from the current clock time, records it, and advances <see cref="Version"/>
    /// by one.
    /// </summary>
    /// <param name="factory">Builds the event from the timestamp it should carry.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="factory"/> is <see langword="null"/>, or it returned <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">No clock is attached; see <see cref="Now"/>.</exception>
    protected void RaiseDomainEvent(Func<DateTimeOffset, IDomainEvent> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        RaiseDomainEvent(factory(Now) ?? throw new ArgumentNullException(nameof(factory), "The event factory returned null."));
    }

    /// <summary>Throws when <paramref name="rule"/> is broken.</summary>
    /// <param name="rule">The rule to enforce.</param>
    /// <exception cref="ArgumentNullException"><paramref name="rule"/> is <see langword="null"/>.</exception>
    /// <exception cref="BusinessRuleViolationException"><paramref name="rule"/> is broken.</exception>
    protected static void CheckRule(IBusinessRule rule) => DomainInvariants.CheckRule(rule);

    /// <summary>
    /// Runs <paramref name="factory"/> and turns a domain failure during construction into a failed
    /// <see cref="ValidationResult{T}"/> instead of an exception.
    /// </summary>
    /// <typeparam name="T">The type the factory creates.</typeparam>
    /// <param name="factory">The construction delegate, typically <c>() =&gt; new Order(...)</c>.</param>
    /// <returns>
    /// A successful result holding the created object; or a failed result holding every error from a
    /// <see cref="ValidationException"/>, or the single error of any <see cref="DomainException"/>, which
    /// includes <see cref="BusinessRuleViolationException"/> and guard violations.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="factory"/> is <see langword="null"/>.</exception>
    /// <remarks>Any other exception propagates: it signals a defect, not invalid input.</remarks>
    protected static ValidationResult<T> TryCreate<T>(Func<T> factory) => DomainInvariants.TryCreate(factory);
}
