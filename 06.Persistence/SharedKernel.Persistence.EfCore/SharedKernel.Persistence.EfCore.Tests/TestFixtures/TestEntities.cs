using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.Aggregates;
using SharedKernel.Domain.Events;
using SharedKernel.Domain.StronglyTypedIds;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Persistence.EfCore.Tests.TestFixtures;

// ---------------------------------------------------------------------------
// Strongly-typed ID
// ---------------------------------------------------------------------------

public sealed record TestId(Guid Value) : StronglyTypedId<Guid>(Value)
{
    public static TestId New() => new(Guid.NewGuid());
}

// ---------------------------------------------------------------------------
// Simple aggregate (write/read tests)
// ---------------------------------------------------------------------------

public sealed class TestAggregate : AggregateRoot<TestId>
{
    public string Name { get; private set; } = string.Empty;

    public TestAggregate(TestId id, string name, IClock clock) : base(id, clock)
    {
        Name = name;
    }

    protected TestAggregate() { } // ORM path
}

// ---------------------------------------------------------------------------
// Auditable aggregate (audit interceptor tests)
// ---------------------------------------------------------------------------

public sealed class AuditableTestAggregate : AuditableSoftDeletableAggregateRoot<TestId>
{
    public string Name { get; private set; } = string.Empty;

    public AuditableTestAggregate(TestId id, string name, IClock clock) : base(id, clock)
    {
        Name = name;
    }

    protected AuditableTestAggregate() { } // ORM path

    protected override void OnDelete() { }

    /// <summary>Raises a test domain event for dispatch tests.</summary>
    public void RaiseTestEvent()
        => RaiseDomainEvent(ts => new TestDomainEvent { OccurredOn = ts });
}

// ---------------------------------------------------------------------------
// Test domain event
// ---------------------------------------------------------------------------

public sealed record TestDomainEvent : DomainEvent;

// ---------------------------------------------------------------------------
// Full-auditable, concurrency-token-bearing aggregate (WO-051/P-315 —
// ConcurrencyInterceptor's provider-neutral SQLite proof; see ConcurrencyInterceptorTests)
// ---------------------------------------------------------------------------

public sealed class ConcurrentTestAggregate : FullAuditableAggregateRoot<TestId>
{
    public string Name { get; private set; } = string.Empty;

    public ConcurrentTestAggregate(TestId id, string name, IClock clock) : base(id, clock)
    {
        Name = name;
    }

    protected ConcurrentTestAggregate() { } // ORM path

    protected override void OnDelete() { }

    /// <summary>Test-only mutator so a real UPDATE statement (beyond RowVersion) is issued.</summary>
    public void Rename(string name) => Name = name;
}

// ---------------------------------------------------------------------------
// Keyset pagination aggregate (WO-051/P-317) — a directly-controllable long sort key,
// avoiding SQLite's lack of ORDER BY support for DateTimeOffset (a provider-specific test
// limitation, not a defect in the keyset seek-predicate algorithm itself).
// ---------------------------------------------------------------------------

public sealed class KeysetTestAggregate : AggregateRoot<TestId>
{
    public string Name { get; private set; } = string.Empty;
    public long SequenceNumber { get; private set; }

    public KeysetTestAggregate(TestId id, string name, long sequenceNumber, IClock clock)
        : base(id, clock)
    {
        Name = name;
        SequenceNumber = sequenceNumber;
    }

    protected KeysetTestAggregate() { } // ORM path
}

// ---------------------------------------------------------------------------
// Non-soft-deletable aggregate (pass-through test)
// ---------------------------------------------------------------------------

public sealed class HardDeleteAggregate : AggregateRoot<TestId>
{
    public string Title { get; private set; } = string.Empty;

    public HardDeleteAggregate(TestId id, string title, IClock clock) : base(id, clock)
    {
        Title = title;
    }

    protected HardDeleteAggregate() { } // ORM path
}

// ---------------------------------------------------------------------------
// Tenanted aggregate (multi-tenancy tests)
// ---------------------------------------------------------------------------

public sealed record TenantedTestId(Guid Value) : StronglyTypedId<Guid>(Value)
{
    public static TenantedTestId New() => new(Guid.NewGuid());
}

public sealed class TenantedTestAggregate : AggregateRoot<TenantedTestId>, IHasTenant
{
    public string Name { get; private set; } = string.Empty;
    public Guid TenantId { get; private set; }

    public TenantedTestAggregate(TenantedTestId id, string name, Guid tenantId, IClock clock)
        : base(id, clock)
    {
        Name = name;
        TenantId = tenantId;
    }

    protected TenantedTestAggregate() { } // ORM path
}

// ---------------------------------------------------------------------------
// Soft-deletable tenanted aggregate (TenantedRepository soft-delete tests)
// ---------------------------------------------------------------------------

public sealed class SoftDeletableTenantedAggregate
    : AuditableSoftDeletableAggregateRoot<TenantedTestId>, IHasTenant
{
    public string Name { get; private set; } = string.Empty;
    public Guid TenantId { get; private set; }

    public SoftDeletableTenantedAggregate(
        TenantedTestId id, string name, Guid tenantId, IClock clock)
        : base(id, clock)
    {
        Name = name;
        TenantId = tenantId;
    }

    protected SoftDeletableTenantedAggregate() { } // ORM path

    protected override void OnDelete() { }
}
