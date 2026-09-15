using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using SharedKernel.Core.Exceptions;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.Aggregates;
using SharedKernel.Domain.BusinessRules;
using SharedKernel.Domain.Entities;
using SharedKernel.Domain.Events;
using SharedKernel.Domain.Exceptions;
using SharedKernel.Domain.Policies;
using SharedKernel.Domain.Specifications;
using SharedKernel.Domain.StronglyTypedIds;
using SharedKernel.Domain.StronglyTypedIds.Serialization;
using SharedKernel.Domain.ValueObjects;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Domain.Tests;

/// <summary>
/// Pins the behaviour fixed and added in the pre-publish hardening pass. Each section names the defect
/// it guards against; the pre-fix behaviour is noted where it was measured.
/// </summary>
public sealed class DomainHardeningTests
{
    private static readonly DateTimeOffset T0 = new(2026, 5, 1, 9, 30, 0, TimeSpan.Zero);

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
        public DateOnly Today => DateOnly.FromDateTime(now.UtcDateTime);
    }

    private sealed record Deleted(string By) : DomainEvent;

    private sealed class Customer : SoftDeletableAggregateRoot<Guid>
    {
        public Customer(Guid id, IClock clock) : base(id, clock) { }

        public Customer() { }

        public void Close(string by) => MarkAsDeleted(by);

        protected override void OnDelete() => RaiseDomainEvent(at => new Deleted(DeletedBy!) { OccurredOn = at });
    }

    // ---- Clock: loaded aggregates never stamp year 0001 ----

    [Fact]
    public void LoadedAggregate_WithoutClock_CannotSoftDelete()
    {
        var customer = new Customer();

        var act = () => customer.Close("admin");

        act.Should().Throw<InvalidOperationException>();
        customer.IsDeleted.Should().BeFalse("state must not change when the time cannot be read");
    }

    [Fact]
    public void LoadedAggregate_AfterAttachClock_UsesTheAttachedClock()
    {
        var customer = new Customer();
        ((IHasClock)customer).AttachClock(new FixedClock(T0));

        customer.Close("admin");

        customer.DeletedOn.Should().Be(T0);
        customer.DomainEvents.Single().OccurredOn.Should().Be(T0);
    }

    [Fact]
    public void AttachClock_Null_Throws()
    {
        var act = () => ((IHasClock)new Customer()).AttachClock(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ConstructedAggregate_ReportsClockAttached() =>
        ((IHasClock)new Customer(Guid.NewGuid(), new FixedClock(T0))).IsClockAttached.Should().BeTrue();

    [Fact]
    public void RaiseDomainEvent_NullEventOrFactory_Throws()
    {
        var aggregate = new Raiser(new FixedClock(T0));

        aggregate.Invoking(a => a.RaiseNull()).Should().Throw<ArgumentNullException>();
        aggregate.Invoking(a => a.RaiseFromFactoryReturningNull()).Should().Throw<ArgumentNullException>();
        aggregate.Version.Should().Be(0);
    }

    private sealed class Raiser(IClock clock) : AggregateRoot<Guid>(Guid.NewGuid(), clock)
    {
        public void RaiseNull() => RaiseDomainEvent((IDomainEvent)null!);

        public void RaiseFromFactoryReturningNull() => RaiseDomainEvent(_ => null!);

        public void Raise() => RaiseDomainEvent(at => new Deleted("x") { OccurredOn = at });
    }

    [Fact]
    public void Version_IsTheSequenceNumberOfTheLastRaisedEvent_AndSurvivesClearing()
    {
        var aggregate = new Raiser(new FixedClock(T0));

        aggregate.Raise();
        aggregate.Raise();
        aggregate.ClearDomainEvents();
        aggregate.Raise();

        aggregate.Version.Should().Be(3);
        aggregate.DomainEvents.Should().ContainSingle();
    }

    // ---- Soft delete: idempotent, validated ----

    [Fact]
    public void MarkAsDeleted_Twice_KeepsTheOriginalDeletion_AndRaisesOneEvent()
    {
        // Before: two events, and the second call overwrote DeletedBy.
        var customer = new Customer(Guid.NewGuid(), new FixedClock(T0));

        customer.Close("first");
        customer.Close("second");

        customer.DeletedBy.Should().Be("first");
        customer.DomainEvents.Should().ContainSingle();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void MarkAsDeleted_WithoutActor_Throws(string? by)
    {
        var customer = new Customer(Guid.NewGuid(), new FixedClock(T0));

        var act = () => customer.Close(by!);

        act.Should().Throw<DomainException>();
        customer.IsDeleted.Should().BeFalse();
    }

    private sealed class Line : SoftDeletableEntity<int>
    {
        public Line(int id) : base(id) { }

        public void Remove(string by, DateTimeOffset at) => MarkAsDeleted(by, at);
    }

    private sealed class Attachment : FullAuditableEntity<int>
    {
        public Attachment(int id) : base(id) { }

        public void Remove(string by, DateTimeOffset at) => MarkAsDeleted(by, at);
    }

    [Fact]
    public void Entity_SoftDelete_UsesTheSuppliedTime_AndIsIdempotent()
    {
        var line = new Line(1);

        line.Remove("owner", T0);
        line.Remove("other", T0.AddDays(1));

        line.IsDeleted.Should().BeTrue();
        line.DeletedOn.Should().Be(T0);
        line.DeletedBy.Should().Be("owner");
    }

    [Fact]
    public void FullAuditableEntity_CanBeSoftDeleted()
    {
        // Before: its soft-delete properties had private setters and no method wrote them.
        var attachment = new Attachment(1);

        attachment.Remove("owner", T0);

        attachment.IsDeleted.Should().BeTrue();
        attachment.Should().BeAssignableTo<IHasConcurrency>().And.BeAssignableTo<IHasAudit>();
    }

    [Fact]
    public void Entity_SoftDelete_NonUtcTime_Throws()
    {
        var act = () => new Line(1).Remove("owner", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.FromHours(3)));

        act.Should().Throw<DomainException>();
    }

    // ---- Tenants ----

    private sealed class T1(Guid tenant) : TenantedAggregateRoot<Guid>(Guid.NewGuid(), tenant, new FixedClock(T0));
    private sealed class T2(Guid tenant) : TenantedAuditableAggregateRoot<Guid>(Guid.NewGuid(), tenant, new FixedClock(T0));
    private sealed class T3(Guid tenant) : TenantedSoftDeletableAggregateRoot<Guid>(Guid.NewGuid(), tenant, new FixedClock(T0))
    {
        protected override void OnDelete() { }
    }
    private sealed class T4(Guid tenant) : TenantedAuditableSoftDeletableAggregateRoot<Guid>(Guid.NewGuid(), tenant, new FixedClock(T0))
    {
        protected override void OnDelete() { }
    }
    private sealed class T5(Guid tenant) : TenantedFullAuditableAggregateRoot<Guid>(Guid.NewGuid(), tenant, new FixedClock(T0))
    {
        protected override void OnDelete() { }
    }

    public static TheoryData<Func<Guid, IHasTenant>> TenantedBases => new()
    {
        t => new T1(t),
        t => new T2(t),
        t => new T3(t),
        t => new T4(t),
        t => new T5(t),
    };

    [Theory]
    [MemberData(nameof(TenantedBases))]
    public void TenantedBase_EmptyTenant_Throws(Func<Guid, IHasTenant> create)
    {
        // Before: Guid.Empty was accepted, creating an aggregate that belonged to no tenant.
        var act = () => create(Guid.Empty);

        act.Should().Throw<DomainException>();
    }

    [Theory]
    [MemberData(nameof(TenantedBases))]
    public void TenantedBase_KeepsTheTenant(Func<Guid, IHasTenant> create)
    {
        var tenant = Guid.NewGuid();

        create(tenant).TenantId.Should().Be(tenant);
    }

    [Fact]
    public void NewTenantedSoftDeleteBases_ImplementTheExpectedInterfaces()
    {
        new T3(Guid.NewGuid()).Should().BeAssignableTo<ISoftDeletable>().And.NotBeAssignableTo<IHasAudit>();
        new T4(Guid.NewGuid()).Should().BeAssignableTo<ISoftDeletable>().And.BeAssignableTo<IHasAudit>()
            .And.NotBeAssignableTo<IHasConcurrency>();
    }

    // ---- Entity identity via the interface ----

    [Fact]
    public void IEntity_ExposesTheId()
    {
        IEntity<int> entity = new Line(7);

        entity.Id.Should().Be(7);
    }

    // ---- Business rules ----

    private sealed class Rule(string code, bool broken) : IBusinessRule
    {
        public string Code => code;
        public string Message => $"{code} message";
        public bool IsBroken() => broken;
    }

    [Fact]
    public void BrokenRule_ErrorCarriesTheRulesCode()
    {
        // Before: every violation reported domain.rule.violated.
        var ex = new BusinessRuleViolationException(new Rule("order.not_paid", true));

        ex.Error.Code.Should().Be("order.not_paid");
        ex.Error.Type.Should().Be(ErrorType.BusinessRule);
    }

    [Fact]
    public void AndRule_ReportsTheCodeOfTheBrokenOperand()
    {
        new Rule("a", false).And(new Rule("b", true)).Code.Should().Be("b");
        new Rule("a", true).And(new Rule("b", true)).Code.Should().Be("a");
    }

    [Fact]
    public void CheckRule_NullRule_ThrowsArgumentNullException()
    {
        var act = () => new BusinessRuleViolationException(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Not_RequiresItsOwnCodeAndMessage()
    {
        var act = () => new Rule("a", false).Not(" ", "message");

        act.Should().Throw<ArgumentException>();
    }

    // ---- Policies ----

    private sealed class MinimumOrders(int minimum) : IPolicy<int>
    {
        public bool IsCompliant(int orders) => orders >= minimum;

        public string Explain(int orders) => IsCompliant(orders) ? string.Empty : $"Needs {minimum} orders.";
    }

    [Fact]
    public void Policy_ToRule_IsBrokenWhenNotCompliant_WithTheExplanationAndCode()
    {
        var rule = new MinimumOrders(5).ToRule(3, "discount.not_eligible");

        rule.IsBroken().Should().BeTrue();
        rule.Code.Should().Be("discount.not_eligible");
        rule.Message.Should().Be("Needs 5 orders.");
        new MinimumOrders(5).ToRule(6, "discount.not_eligible").IsBroken().Should().BeFalse();
    }

    [Fact]
    public void Policy_ToRule_BlankCode_Throws()
    {
        var act = () => new MinimumOrders(5).ToRule(3, "");

        act.Should().Throw<ArgumentException>();
    }

    // ---- Specifications ----

    private sealed class Item
    {
        public bool Active { get; init; }
        public int Tenant { get; init; }
        public int Id { get; init; }
        public DateTimeOffset Created { get; init; }
    }

    private sealed class ActiveInTenant : Specification<Item>
    {
        public ActiveInTenant()
        {
            AddCriteria(i => i.Active);
            AddCriteria(x => x.Tenant == 1);
        }
    }

    [Fact]
    public void AddCriteria_Twice_CombinesWithAnd()
    {
        // Before: the second call replaced the first, so an inactive item matched.
        var spec = new ActiveInTenant();

        spec.IsSatisfiedBy(new Item { Active = false, Tenant = 1 }).Should().BeFalse();
        spec.IsSatisfiedBy(new Item { Active = true, Tenant = 2 }).Should().BeFalse();
        spec.IsSatisfiedBy(new Item { Active = true, Tenant = 1 }).Should().BeTrue();
    }

    private sealed class LateCriteria : Specification<Item>
    {
        public LateCriteria() => AddCriteria(i => i.Active);

        public void AddTenant() => AddCriteria(i => i.Tenant == 1);
    }

    [Fact]
    public void AddCriteria_AfterEvaluation_InvalidatesTheCompiledPredicate()
    {
        var spec = new LateCriteria();
        spec.IsSatisfiedBy(new Item { Active = true, Tenant = 2 }).Should().BeTrue();

        spec.AddTenant();

        spec.IsSatisfiedBy(new Item { Active = true, Tenant = 2 }).Should().BeFalse();
    }

    private sealed class TwoPrimarySorts : Specification<Item>
    {
        public TwoPrimarySorts()
        {
            ApplyOrderBy(i => i.Tenant);
            ApplyOrderByDescending(i => i.Id);
        }
    }

    [Fact]
    public void SecondPrimarySort_Throws() =>
        FluentActions.Invoking(() => new TwoPrimarySorts()).Should().Throw<InvalidOperationException>();

    private sealed class Paged(int page, int size) : PagedSpecification<Item>(page, size);

    [Fact]
    public void PagedSpecification_OffsetBeyondInt32_Throws()
    {
        // Before: Skip overflowed to a negative number.
        var act = () => new Paged(int.MaxValue / 2, 1000);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private sealed class ByCreated(bool descending) : KeysetSpecification<Item, DateTimeOffset>(
        i => i.Created, i => i.Id, null, null, descending, 10);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Keyset_IdTiebreak_FollowsThePrimarySortDirection(bool descending)
    {
        // Before: always ascending, while the persistence seek predicate flips to "<" when descending,
        // which skipped or repeated rows sharing a sort key.
        new ByCreated(descending).ThenBys.Should().ContainSingle().Which.Descending.Should().Be(descending);
    }

    private sealed class WithInclude : Specification<Item>
    {
        public static readonly Expression<Func<Item, object>> Nav = i => i.Tenant;

        public WithInclude()
        {
            AddInclude(Nav);
            AddStringInclude("A.B");
        }
    }

    [Fact]
    public void Composites_DoNotDuplicateSharedIncludes()
    {
        var left = new WithInclude();
        var right = new WithInclude();

        var and = left.And(right);
        var or = left.Or(right);

        and.Includes.Should().ContainSingle();
        and.StringIncludes.Should().ContainSingle();
        or.StringIncludes.Should().ContainSingle();
    }

    // ---- Domain events ----

    [Fact]
    public void DomainEventId_SurvivesJsonRoundTrip()
    {
        // Before: Id was get-only, so deserialization generated a new Guid and broke deduplication.
        var original = new Deleted("x") { OccurredOn = T0 };

        var copy = JsonSerializer.Deserialize<Deleted>(JsonSerializer.Serialize(original))!;

        copy.Id.Should().Be(original.Id);
    }

    [Fact]
    public void DomainEventId_IsVersion7() => new Deleted("x") { OccurredOn = T0 }.Id.Version.Should().Be(7);

    [DomainEventVersion(3)]
    private sealed record Versioned : DomainEvent;

    [Fact]
    public void GetVersion_Generic_ReadsTheAttribute() => DomainEventVersionHelper.GetVersion<Versioned>().Should().Be(3);

    // ---- Value objects ----

    private sealed class Tags : ValueObject
    {
        public Tags(params string[] values) => Values = values;

        public IReadOnlyList<string> Values { get; }

        protected override IEnumerable<object?> GetEqualityComponents()
        {
            yield return Values;
        }

        protected override IEnumerable<Error>? Validate() => null;
    }

    [Fact]
    public void CollectionComponent_ComparesByContent()
    {
        // Before: compared the list references, so equal contents were unequal.
        new Tags("a", "b").Should().Be(new Tags("a", "b"));
        new Tags("a", "b").GetHashCode().Should().Be(new Tags("a", "b").GetHashCode());
        new Tags("a", "b").Should().NotBe(new Tags("b", "a"));
    }

    // ---- Strongly-typed identifiers ----

    private sealed record OrderId(Guid Value) : StronglyTypedId<Guid>(Value);

    private sealed record Code(string Value) : StronglyTypedId<string>(Value);

    private sealed record StampId(DateTimeOffset Value) : StronglyTypedId<DateTimeOffset>(Value);

    private static readonly JsonSerializerOptions JsonOptions = new() { Converters = { new StronglyTypedIdJsonConverterFactory() } };

    [Fact]
    public void StronglyTypedId_HasNoImplicitConversion()
    {
        typeof(StronglyTypedId<Guid>).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Should().NotContain(m => m.Name == "op_Implicit");
        typeof(SingleValueObject<string>).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Should().NotContain(m => m.Name == "op_Implicit");
    }

    [Fact]
    public void StronglyTypedId_ExplicitCastOfNull_ThrowsArgumentNullException()
    {
        var act = () => (Guid)(OrderId)null!;

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void StronglyTypedId_NullValue_UsesTheRequiredErrorCode()
    {
        var act = () => new Code(null!);

        act.Should().Throw<DomainException>().Which.Error.Code.Should().Be(ErrorCodes.Validation.Required);
    }

    [Fact]
    public void StronglyTypedId_AsDictionaryKey_RoundTrips()
    {
        // Before: NotSupportedException, the converter implemented no property-name methods.
        var id = new OrderId(Guid.NewGuid());
        var map = new Dictionary<OrderId, int> { [id] = 5 };

        var json = JsonSerializer.Serialize(map, JsonOptions);
        var copy = JsonSerializer.Deserialize<Dictionary<OrderId, int>>(json, JsonOptions)!;

        json.Should().Be($"{{\"{id.Value}\":5}}");
        copy[id].Should().Be(5);
    }

    [Fact]
    public void StronglyTypedId_AnySerializableValueType_IsSupported()
    {
        // Before: only Guid, int, long and string were converted.
        var id = new StampId(T0);

        var copy = JsonSerializer.Deserialize<StampId>(JsonSerializer.Serialize(id, JsonOptions), JsonOptions);

        copy.Should().Be(id);
    }

    [Fact]
    public void StronglyTypedId_JsonNull_ReadsAsNull() =>
        JsonSerializer.Deserialize<OrderId>("null", JsonOptions).Should().BeNull();

    // ---- Exceptions ----

    [Fact]
    public void DomainNotFoundException_NullArguments_Throw()
    {
        FluentActions.Invoking(() => new DomainNotFoundException(null!, 1)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new DomainNotFoundException(typeof(Customer), null!)).Should().Throw<ArgumentNullException>();
    }
}
