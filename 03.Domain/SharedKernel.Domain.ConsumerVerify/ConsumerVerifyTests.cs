using System.Linq.Expressions;
using System.Text.Json;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.Aggregates;
using SharedKernel.Domain.BusinessRules;
using SharedKernel.Domain.Entities;
using SharedKernel.Domain.Events;
using SharedKernel.Domain.Policies;
using SharedKernel.Domain.Specifications;
using SharedKernel.Domain.StronglyTypedIds;
using SharedKernel.Domain.StronglyTypedIds.Serialization;
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
    // StronglyTypedId<TValue> — explicit operator and ToString
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void StronglyTypedId_ExplicitOperator_UnwrapsValue_ResolvedFromPackage()
    {
        var raw = Guid.NewGuid();
        var id = new OrderId(raw);

        var unwrapped = (Guid)id;

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

    // ──────────────────────────────────────────────────────────────────────────
    // StronglyTypedIdJsonConverterFactory — opt-in STJ bare-primitive serialization
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void StronglyTypedIdJsonConverterFactory_RoundTrips_BarePrimitiveWireFormat_ResolvedFromPackage()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new StronglyTypedIdJsonConverterFactory());

        var id = new OrderId(Guid.NewGuid());

        var json = JsonSerializer.Serialize(id, options);
        var roundTripped = JsonSerializer.Deserialize<OrderId>(json, options);

        Assert.Equal($"\"{id.Value}\"", json);
        Assert.Equal(id, roundTripped);
    }

    [Fact]
    public void StronglyTypedIdJsonConverterFactory_CanConvert_FalseForUnrelatedType_ResolvedFromPackage()
    {
        var factory = new StronglyTypedIdJsonConverterFactory();

        Assert.False(factory.CanConvert(typeof(string)));
        Assert.True(factory.CanConvert(typeof(OrderId)));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // WO-051 (P-307..P-313) — new public surface
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CompositeSpecification_UnionsIncludesAndStringIncludes_ResolvedFromPackage()
    {
        // P-307 regression: AndSpecification/OrSpecification/NotSpecification must union
        // Includes and StringIncludes from their operand(s), not drop them.
        var withInclude = new WithIncludeSpec();
        var withStringInclude = new WithStringIncludeSpec();

        var andCombined = withInclude.And(withStringInclude);
        Assert.Single(andCombined.Includes);
        Assert.Single(andCombined.StringIncludes);

        var orCombined = withInclude.Or(withStringInclude);
        Assert.Single(orCombined.Includes);
        Assert.Single(orCombined.StringIncludes);

        var notCombined = withInclude.Not();
        Assert.Single(notCombined.Includes);
    }

    [Fact]
    public void KeysetSpecification_FirstPage_OrdersByKeyThenId_ResolvedFromPackage()
    {
        var spec = new OrderKeysetSpec(afterKey: null, afterId: null, take: 10);

        Assert.Null(spec.AfterKey);
        Assert.Null(spec.AfterId);
        Assert.False(spec.Descending);
        Assert.Equal(0, spec.Skip);
        Assert.Equal(10, spec.Take);
    }

    [Fact]
    public void KeysetSpecification_PartialCursor_ThrowsArgumentException_ResolvedFromPackage()
    {
        Assert.Throws<ArgumentException>(() => new OrderKeysetSpec(afterKey: 5, afterId: null, take: 10));
    }

    [Fact]
    public void ISpecification_AsSplitQuery_DefaultsFalse_AndComposesLikeAsNoTracking_ResolvedFromPackage()
    {
        var plain = new ActiveOrderSpec();
        Assert.False(plain.AsSplitQuery);

        var splitSpec = new SplitQueryOrderSpec();
        var combined = plain.And(splitSpec);
        Assert.True(combined.AsSplitQuery);
    }

    [Fact]
    public void IHasAggregateId_ConcreteEvent_ExposesAggregateId_ResolvedFromPackage()
    {
        var orderId = Guid.NewGuid();
        IDomainEvent evt = new OrderShippedEvent(orderId) { OccurredOn = DateTimeOffset.UtcNow };

        Assert.True(evt is IHasAggregateId<Guid>);
        Assert.Equal(orderId, ((IHasAggregateId<Guid>)evt).AggregateId);
    }

    [Fact]
    public void ValueObject_TryCreate_SuccessPath_ResolvedFromPackage()
    {
        var result = Quantity.Create(5);

        Assert.True(result.IsValid);
        Assert.Equal(5, result.Value.Amount);
    }

    [Fact]
    public void ValueObject_TryCreate_ValidationFailure_ReturnsFailureResult_ResolvedFromPackage()
    {
        var result = Quantity.Create(-1);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void IPolicy_Explain_DefaultInterfaceMember_ResolvedFromPackage()
    {
        // A policy that only implements IsCompliant still compiles and returns the DIM default.
        IPolicy<string> policy = new MinimalPolicy();

        Assert.Equal(string.Empty, policy.Explain("compliant"));
        Assert.Contains("MinimalPolicy", policy.Explain("noncompliant"));
    }

    [Fact]
    public void Specification_Create_AdHocFactory_ComposesWithNamedSpecification_ResolvedFromPackage()
    {
        var adHoc = Specification<OrderDto>.Create(o => o.Id > 1);
        var combined = adHoc.And(new ActiveOrderSpec());

        var orders = new[]
        {
            new OrderDto(1, true),
            new OrderDto(2, true),
            new OrderDto(3, false),
        };

        var result = orders.AsQueryable().Where(combined.Criteria!).ToList();

        Assert.Single(result);
        Assert.Equal(2, result[0].Id);
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
    public string Code => "test.rule";
    public string Message => "Always passes.";
    public bool IsBroken() => false;
}

internal sealed class AlwaysFailRule : IBusinessRule
{
    public string Code => "test.rule";
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

// ──────────────────────────────────────────────────────────────────────────
// WO-051 (P-307..P-313) test-local helpers
// ──────────────────────────────────────────────────────────────────────────

// P-307 — composite spec Includes/StringIncludes union propagation
internal sealed class WithIncludeSpec : Specification<OrderDto>
{
    public WithIncludeSpec() => AddInclude(o => (object)o.IsActive);
}

internal sealed class WithStringIncludeSpec : Specification<OrderDto>
{
    public WithStringIncludeSpec() => AddStringInclude("SomeNavigation");
}

// P-308a — KeysetSpecification<T, TKey>
internal sealed class OrderKeysetSpec : KeysetSpecification<OrderDto, int>
{
    public OrderKeysetSpec(int? afterKey, object? afterId, int take)
        : base(o => o.Id, o => o.Id, afterKey, afterId, descending: false, take)
    {
    }
}

// P-308b — ISpecification<T>.AsSplitQuery
internal sealed class SplitQueryOrderSpec : Specification<OrderDto>
{
    public SplitQueryOrderSpec() => ApplySplitQuery();
}

// P-309 — IHasAggregateId<TId>
internal sealed record OrderShippedEvent(Guid AggregateId) : DomainEvent, IHasAggregateId<Guid>;

// P-310 — ValueObject.TryCreate<T>/CheckRule
// Assigns in the constructor body and calls EnsureValid last, the pattern ValueObject requires.
internal sealed class Quantity : ValueObject
{
    private Quantity(int amount)
    {
        Amount = amount;
        EnsureValid();
    }

    public int Amount { get; }

    public static SharedKernel.Primitives.Results.ValidationResult<Quantity> Create(int amount) =>
        TryCreate(() => new Quantity(amount));

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Amount;
    }

    protected override IEnumerable<SharedKernel.Primitives.Errors.Error>? Validate()
    {
        if (Amount < 0)
            yield return SharedKernel.Primitives.Errors.Error.Validation(
                "Quantity.Negative", "Amount must be non-negative.");
    }
}

// P-312 — IPolicy<T>.Explain default interface member (zero-breaking-change proof)
internal sealed class MinimalPolicy : IPolicy<string>
{
    public bool IsCompliant(string subject) => subject == "compliant";
}
