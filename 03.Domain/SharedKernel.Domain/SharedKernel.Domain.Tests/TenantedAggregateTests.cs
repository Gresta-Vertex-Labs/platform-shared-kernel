using System.Reflection;
using FluentAssertions;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.Aggregates;
using SharedKernel.Domain.Events;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Domain.Tests;

/// <summary>
/// T-17: P-044/WO-010 — Tenanted aggregate root tests.
/// </summary>
public class TenantedAggregateTests
{
    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
        public DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
    }

    private sealed record DeletedEvent : DomainEvent
    {
        public Guid AggregateId { get; init; }
    }

    private sealed class TenantOrder : TenantedAggregateRoot<Guid>
    {
        public TenantOrder(Guid id, Guid tenantId, IClock clock) : base(id, tenantId, clock) { }
        public TenantOrder() : base() { }
    }

    private sealed class TenantAuditableOrder : TenantedAuditableAggregateRoot<Guid>
    {
        public TenantAuditableOrder(Guid id, Guid tenantId, IClock clock) : base(id, tenantId, clock) { }
        public TenantAuditableOrder() : base() { }
    }

    private sealed class TenantFullOrder : TenantedFullAuditableAggregateRoot<Guid>
    {
        public TenantFullOrder(Guid id, Guid tenantId, IClock clock) : base(id, tenantId, clock) { }
        public TenantFullOrder() : base() { }

        protected override void OnDelete()
        {
            RaiseDomainEvent(ts => new DeletedEvent { AggregateId = Id, OccurredOn = ts });
        }

        public void Delete(string by) => MarkAsDeleted(by);
    }

    // --- TenantedAggregateRoot ---

    [Fact]
    public void TenantedAggregateRoot_TenantId_SetCorrectly()
    {
        var tenantId = Guid.NewGuid();
        var order = new TenantOrder(Guid.NewGuid(), tenantId, new FixedClock());

        order.TenantId.Should().Be(tenantId);
    }

    [Fact]
    public void TenantedAggregateRoot_OrmPath_TenantId_IsEmpty()
    {
        var order = new TenantOrder();

        order.TenantId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void TenantedAggregateRoot_TenantId_Setter_IsPrivate()
    {
        var prop = typeof(TenantedAggregateRoot<Guid>)
            .GetProperty(nameof(TenantedAggregateRoot<Guid>.TenantId));

        prop!.SetMethod!.IsPrivate.Should().BeTrue("TenantId setter must be private");
    }

    // --- TenantedAuditableAggregateRoot ---

    [Fact]
    public void TenantedAuditableAggregateRoot_IsA_AuditableAggregateRoot()
    {
        var order = new TenantAuditableOrder(Guid.NewGuid(), Guid.NewGuid(), new FixedClock());

        order.Should().BeAssignableTo<AuditableAggregateRoot<Guid>>();
    }

    [Fact]
    public void TenantedAuditableAggregateRoot_Implements_IHasTenant()
    {
        var order = new TenantAuditableOrder(Guid.NewGuid(), Guid.NewGuid(), new FixedClock());

        order.Should().BeAssignableTo<IHasTenant>();
    }

    [Fact]
    public void TenantedAuditableAggregateRoot_TenantId_SetCorrectly()
    {
        var tenantId = Guid.NewGuid();
        var order = new TenantAuditableOrder(Guid.NewGuid(), tenantId, new FixedClock());

        order.TenantId.Should().Be(tenantId);
    }

    [Fact]
    public void TenantedAuditableAggregateRoot_OrmPath_TenantId_IsEmpty()
    {
        var order = new TenantAuditableOrder();
        order.TenantId.Should().Be(Guid.Empty);
    }

    // --- TenantedFullAuditableAggregateRoot ---

    [Fact]
    public void TenantedFullAuditableAggregateRoot_IsA_FullAuditableAggregateRoot()
    {
        var order = new TenantFullOrder(Guid.NewGuid(), Guid.NewGuid(), new FixedClock());

        order.Should().BeAssignableTo<FullAuditableAggregateRoot<Guid>>();
    }

    [Fact]
    public void TenantedFullAuditableAggregateRoot_Implements_IHasTenant()
    {
        var order = new TenantFullOrder(Guid.NewGuid(), Guid.NewGuid(), new FixedClock());

        order.Should().BeAssignableTo<IHasTenant>();
    }

    [Fact]
    public void TenantedFullAuditableAggregateRoot_TenantId_SetCorrectly()
    {
        var tenantId = Guid.NewGuid();
        var order = new TenantFullOrder(Guid.NewGuid(), tenantId, new FixedClock());

        order.TenantId.Should().Be(tenantId);
    }

    [Fact]
    public void TenantedFullAuditableAggregateRoot_OrmPath_TenantId_IsEmpty()
    {
        var order = new TenantFullOrder();
        order.TenantId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void TenantedFullAuditableAggregateRoot_MarkAsDeleted_IsInherited()
    {
        var clock = new FixedClock();
        var order = new TenantFullOrder(Guid.NewGuid(), Guid.NewGuid(), clock);

        order.Delete("admin");

        order.IsDeleted.Should().BeTrue();
        order.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<DeletedEvent>();
    }
}
