using SharedKernel.Execution.Tenancy;
using FluentAssertions;
using SharedKernel.Core.Exceptions;
using SharedKernel.Domain.Aggregates;
using SharedKernel.Domain.StronglyTypedIds;
using SharedKernel.Domain.ValueObjects;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Domain.Tests;

/// <summary>
/// T-36: P-311a/WO-051 — Guard-clause adoption tests: AggregateRoot&lt;TId&gt;'s clock parameter
/// (and the three tenanted bases chaining through the same constructor), StronglyTypedId&lt;TValue&gt;
/// and SingleValueObject&lt;TValue&gt;'s reference-type Value guarding.
/// </summary>
public class GuardClauseAdoptionTests
{
    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
        public DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
    }

    private sealed class PlainOrder : AggregateRoot<Guid>
    {
        public PlainOrder(Guid id, IClock clock) : base(id, clock) { }
    }

    private sealed class TenantOrder : TenantedAggregateRoot<Guid>
    {
        public TenantOrder(Guid id, TenantId tenantId, IClock clock) : base(id, tenantId, clock) { }
    }

    private sealed class TenantAuditableOrder : TenantedAuditableAggregateRoot<Guid>
    {
        public TenantAuditableOrder(Guid id, TenantId tenantId, IClock clock) : base(id, tenantId, clock) { }
    }

    private sealed class TenantFullOrder : TenantedFullAuditableAggregateRoot<Guid>
    {
        public TenantFullOrder(Guid id, TenantId tenantId, IClock clock) : base(id, tenantId, clock) { }

        protected override void OnDelete() { }
    }

    private sealed record StringId(string Value) : StronglyTypedId<string>(Value);
    private sealed record GuidId(Guid Value) : StronglyTypedId<Guid>(Value);

    private sealed class NameValue(string value) : SingleValueObject<string>(value)
    {
        protected override IEnumerable<Error>? Validate() => null;
    }

    // --- AggregateRoot<TId>.clock ---

    [Fact]
    public void AggregateRoot_NullClock_ThrowsDomainException()
    {
        var act = () => new PlainOrder(Guid.NewGuid(), null!);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void AggregateRoot_NonNullClock_DoesNotThrow()
    {
        var act = () => new PlainOrder(Guid.NewGuid(), new FixedClock());

        act.Should().NotThrow();
    }

    // --- TenantedAggregateRoot<TId> chains through the same guarded constructor ---

    [Fact]
    public void TenantedAggregateRoot_NullClock_ThrowsDomainException()
    {
        var act = () => new TenantOrder(Guid.NewGuid(), new TenantId(Guid.NewGuid()), null!);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void TenantedAuditableAggregateRoot_NullClock_ThrowsDomainException()
    {
        var act = () => new TenantAuditableOrder(Guid.NewGuid(), new TenantId(Guid.NewGuid()), null!);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void TenantedFullAuditableAggregateRoot_NullClock_ThrowsDomainException()
    {
        var act = () => new TenantFullOrder(Guid.NewGuid(), new TenantId(Guid.NewGuid()), null!);

        act.Should().Throw<DomainException>();
    }

    // --- StronglyTypedId<TValue>.Value — reference-type TValue guarded ---

    [Fact]
    public void StronglyTypedId_ReferenceTypeValue_Null_ThrowsDomainException()
    {
        var act = () => new StringId(null!);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void StronglyTypedId_ReferenceTypeValue_NonNull_DoesNotThrow()
    {
        var act = () => new StringId("abc");

        act.Should().NotThrow();
    }

    // --- StronglyTypedId<TValue>.Value — value-type TValue is unaffected (no guard fires) ---

    [Fact]
    public void StronglyTypedId_ValueTypeValue_IsUnaffected()
    {
        var guid = Guid.NewGuid();
        var act = () => new GuidId(guid);

        act.Should().NotThrow();
        act().Value.Should().Be(guid);
    }

    // --- SingleValueObject<TValue>.Value — reference-type TValue guarded ---

    [Fact]
    public void SingleValueObject_ReferenceTypeValue_Null_ThrowsDomainException()
    {
        var act = () => new NameValue(null!);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void SingleValueObject_ReferenceTypeValue_NonNull_DoesNotThrow()
    {
        var act = () => new NameValue("Alice");

        act.Should().NotThrow();
    }
}
