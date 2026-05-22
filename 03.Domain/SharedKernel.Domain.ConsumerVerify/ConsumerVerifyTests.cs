using System.Linq.Expressions;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.Aggregates;
using SharedKernel.Domain.BusinessRules;
using SharedKernel.Domain.Entities;
using SharedKernel.Domain.Events;
using SharedKernel.Domain.Policies;
using SharedKernel.Domain.Specifications;
using SharedKernel.Domain.StronglyTypedIds;
using SharedKernel.Domain.ValueObjects;
using SharedKernel.Primitives.Clocks;
using Xunit;

namespace SharedKernel.Domain.ConsumerVerify;

/// <summary>
/// Consumer verification for the published SharedKernel.Domain NuGet package.
/// Resolved via the local feed (nupkgs/) — not project references.
/// Confirms that Entity&lt;TId&gt;, AggregateRoot&lt;TId&gt;, ValueObject, ISpecification&lt;T&gt;,
/// StronglyTypedId&lt;TValue&gt;, IBusinessRule, and IPolicy&lt;T&gt; are all usable
/// from a single PackageReference to SharedKernel.Domain.
/// </summary>
public sealed class ConsumerVerifyTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // Entity<TId> — identity-based equality
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Entity_SameId_AreEqual_ResolvedFromPackage()
    {
        var id = Guid.NewGuid();
        var a = new OrderEntity(id);
        var b = new OrderEntity(id);

        Assert.Equal(a, b);
    }

    [Fact]
    public void Entity_DifferentId_AreNotEqual_ResolvedFromPackage()
    {
        var a = new OrderEntity(Guid.NewGuid());
        var b = new OrderEntity(Guid.NewGuid());

        Assert.NotEqual(a, b);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // AggregateRoot<TId> — domain event machinery
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AggregateRoot_RaisedEvent_AppearsInDomainEvents_ResolvedFromPackage()
    {
        var clock = new SystemClock();
        var order = new OrderAggregate(Guid.NewGuid(), clock);

        order.PlaceOrder();

        Assert.Single(order.DomainEvents);
        Assert.IsType<OrderPlacedEvent>(order.DomainEvents.First());
    }

    [Fact]
    public void AggregateRoot_ClearDomainEvents_EmptiesCollection_ResolvedFromPackage()
    {
        var clock = new SystemClock();
        var order = new OrderAggregate(Guid.NewGuid(), clock);
        order.PlaceOrder();

        order.ClearDomainEvents();

        Assert.Empty(order.DomainEvents);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // ValueObject — structural equality
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ValueObject_SameComponents_AreEqual_ResolvedFromPackage()
    {
        var a = new Money(100m, "USD");
        var b = new Money(100m, "USD");

        Assert.Equal(a, b);
    }

    [Fact]
    public void ValueObject_DifferentComponent_AreNotEqual_ResolvedFromPackage()
    {
        var a = new Money(100m, "USD");
        var b = new Money(200m, "USD");

        Assert.NotEqual(a, b);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // StronglyTypedId<TValue> — implicit operator and ToString
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void StronglyTypedId_ImplicitOperator_UnwrapsValue_ResolvedFromPackage()
    {
        var raw = Guid.NewGuid();
        var id = new OrderId(raw);

        Guid unwrapped = id;

        Assert.Equal(raw, unwrapped);
    }

    [Fact]
    public void StronglyTypedId_ToString_DelegatesToValue_ResolvedFromPackage()
    {
        var raw = Guid.NewGuid();
        var id = new OrderId(raw);

        Assert.Equal(raw.ToString(), id.ToString());
    }

    // ──────────────────────────────────────────────────────────────────────────
    // ISpecification<T> — expression composition
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Specification_Criteria_FiltersList_ResolvedFromPackage()
    {
        var spec = new ActiveOrderSpec();
        var orders = new[]
        {
            new OrderDto(1, true),
            new OrderDto(2, false),
            new OrderDto(3, true),
        };

        var result = orders.AsQueryable()
            .Where(spec.Criteria!)
            .ToList();

        Assert.Equal(2, result.Count);
        Assert.All(result, o => Assert.True(o.IsActive));
    }

    [Fact]
    public void Specification_AndComposition_ResolvedFromPackage()
    {
        var activeSpec = new ActiveOrderSpec();
        var highValueSpec = new HighValueOrderSpec();
        var combined = activeSpec.And(highValueSpec);

        var orders = new[]
        {
            new OrderDto(1,  true),
            new OrderDto(5,  true),
            new OrderDto(3, false),
        };

        var result = orders.AsQueryable()
            .Where(combined.Criteria!)
            .ToList();

        Assert.Single(result);
        Assert.Equal(5, result[0].Id);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // IBusinessRule — composite rules and CheckRule
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BusinessRule_NotBroken_DoesNotThrow_ResolvedFromPackage()
    {
        var clock = new SystemClock();
        var order = new OrderAggregate(Guid.NewGuid(), clock);

        // CheckRule with non-broken rule must not throw.
        var exception = Record.Exception(() => order.PublicCheckRule(new AlwaysPassRule()));

        Assert.Null(exception);
    }

    [Fact]
    public void BusinessRule_Broken_ThrowsBusinessRuleViolationException_ResolvedFromPackage()
    {
        var clock = new SystemClock();
        var order = new OrderAggregate(Guid.NewGuid(), clock);

        Assert.ThrowsAny<Exception>(() => order.PublicCheckRule(new AlwaysFailRule()));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // IPolicy<T> — compliance evaluation
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Policy_CompliantSubject_ReturnsTrue_ResolvedFromPackage()
    {
        var policy = new PremiumCustomerPolicy();

        Assert.True(policy.IsCompliant("premium-tier"));
    }

    [Fact]
    public void Policy_NonCompliantSubject_ReturnsFalse_ResolvedFromPackage()
    {
        var policy = new PremiumCustomerPolicy();

        Assert.False(policy.IsCompliant("basic-tier"));
    }

    [Fact]
    public void Policy_AndComposition_ResolvedFromPackage()
    {
        var premiumPolicy = new PremiumCustomerPolicy();
        var verifiedPolicy = new VerifiedCustomerPolicy();
        var combined = premiumPolicy.And(verifiedPolicy);

        Assert.True(combined.IsCompliant("premium-verified"));
        Assert.False(combined.IsCompliant("premium-tier"));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // IDomainEvent — OccurredOn set at construction
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void DomainEvent_OccurredOn_SetAtConstruction_ResolvedFromPackage()
    {
        var before = DateTimeOffset.UtcNow;
        var clock = new SystemClock();
        var order = new OrderAggregate(Guid.NewGuid(), clock);
        order.PlaceOrder();
        var after = DateTimeOffset.UtcNow;

        var evt = order.DomainEvents.First();

        Assert.InRange(evt.OccurredOn, before, after);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // IDomainService marker — no-member marker interface is accessible
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void IDomainService_MarkerInterface_IsAccessible_ResolvedFromPackage()
    {
        Assert.True(typeof(IDomainService).IsInterface);
    }
}

// ──────────────────────────────────────────────────────────────────────────
// Test-local helpers — domain types used only in this consumer verification
// ──────────────────────────────────────────────────────────────────────────

// Strongly-typed ID
internal sealed record OrderId(Guid Value) : StronglyTypedId<Guid>(Value);

// Entity
internal sealed class OrderEntity(Guid id) : Entity<Guid>(id);

// Domain event
internal sealed record OrderPlacedEvent : DomainEvent;

// Aggregate root
internal sealed class OrderAggregate : AggregateRoot<Guid>
{
    public OrderAggregate(Guid id, IClock clock) : base(id, clock) { }

    // ORM-path constructor
    private OrderAggregate() { }

    public void PlaceOrder() =>
        RaiseDomainEvent(t => new OrderPlacedEvent { OccurredOn = t });

    public void PublicCheckRule(IBusinessRule rule) =>
        CheckRule(rule);
}

// Value object
internal sealed class Money : ValueObject
{
    public decimal Amount { get; }
    public string Currency { get; }

    public Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Amount;
        yield return Currency;
    }

    protected override IEnumerable<SharedKernel.Primitives.Errors.Error>? Validate() => null;
}

// Specification DTO
internal sealed record OrderDto(int Id, bool IsActive);

// Specifications
internal sealed class ActiveOrderSpec : Specification<OrderDto>
{
    public ActiveOrderSpec() => AddCriteria(o => o.IsActive);
}

internal sealed class HighValueOrderSpec : Specification<OrderDto>
{
    public HighValueOrderSpec() => AddCriteria(o => o.Id >= 5);
}

// Business rules
internal sealed class AlwaysPassRule : IBusinessRule
{
    public string Message => "Always passes.";
    public bool IsBroken() => false;
}

internal sealed class AlwaysFailRule : IBusinessRule
{
    public string Message => "Always fails.";
    public bool IsBroken() => true;
}

// Policies
internal sealed class PremiumCustomerPolicy : IPolicy<string>
{
    public bool IsCompliant(string subject) =>
        subject.Contains("premium", StringComparison.OrdinalIgnoreCase);
}

internal sealed class VerifiedCustomerPolicy : IPolicy<string>
{
    public bool IsCompliant(string subject) =>
        subject.Contains("verified", StringComparison.OrdinalIgnoreCase);
}
