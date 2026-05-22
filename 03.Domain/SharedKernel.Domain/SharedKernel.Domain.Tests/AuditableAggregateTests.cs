using System.Reflection;
using FluentAssertions;
using SharedKernel.Domain.Aggregates;
using SharedKernel.Domain.Events;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Domain.Tests;

public class AuditableAggregateTests
{
    // --- Test doubles ---

    private sealed class FixedClock : IClock
    {
        private readonly DateTimeOffset _now;
        public FixedClock(DateTimeOffset now) => _now = now;
        public DateTimeOffset UtcNow => _now;
        public DateOnly Today => DateOnly.FromDateTime(_now.UtcDateTime);
    }

    private sealed record DeletedEvent : DomainEvent
    {
        public Guid AggregateId { get; init; }
    }

    private sealed class FullOrder : FullAuditableAggregateRoot<Guid>
    {
        public FullOrder(Guid id, IClock clock) : base(id, clock) { }
        public FullOrder() : base() { }

        protected override void OnDelete()
        {
            RaiseDomainEvent(now => new DeletedEvent { AggregateId = Id, OccurredOn = now });
        }

        public void Delete(string by) => MarkAsDeleted(by);
    }

    private sealed class SoftOrder : SoftDeletableAggregateRoot<Guid>
    {
        public SoftOrder(Guid id, IClock clock) : base(id, clock) { }
        public SoftOrder() : base() { }

        protected override void OnDelete() { }

        public void Delete(string by) => MarkAsDeleted(by);
    }

    // --- SoftDeletableAggregateRoot ---

    [Fact]
    public void MarkAsDeleted_SetsIsDeleted_True()
    {
        var clock = new FixedClock(DateTimeOffset.UtcNow);
        var order = new SoftOrder(Guid.NewGuid(), clock);

        order.Delete("user1");

        order.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public void MarkAsDeleted_SetsDeletedByAndDeletedOn()
    {
        var fixedTime = new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);
        var clock = new FixedClock(fixedTime);
        var order = new SoftOrder(Guid.NewGuid(), clock);

        order.Delete("user1");

        order.DeletedBy.Should().Be("user1");
        order.DeletedOn.Should().Be(fixedTime);
    }

    // --- FullAuditableAggregateRoot ---

    [Fact]
    public void FullAuditable_RowVersion_HasProtectedSet()
    {
        var prop = typeof(FullAuditableAggregateRoot<Guid>)
            .GetProperty(nameof(FullAuditableAggregateRoot<Guid>.RowVersion));
        prop!.SetMethod!.IsFamily.Should().BeTrue("RowVersion setter must be protected");
    }

    [Fact]
    public void FullAuditable_MarkAsDeleted_SetsFields()
    {
        var clock = new FixedClock(DateTimeOffset.UtcNow);
        var order = new FullOrder(Guid.NewGuid(), clock);

        order.Delete("admin");

        order.IsDeleted.Should().BeTrue();
        order.DeletedBy.Should().Be("admin");
    }

    [Fact]
    public void FullAuditable_MarkAsDeleted_RaisesDomainEventViaOnDelete()
    {
        var clock = new FixedClock(DateTimeOffset.UtcNow);
        var order = new FullOrder(Guid.NewGuid(), clock);

        order.Delete("admin");

        order.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<DeletedEvent>();
    }

    // --- Audit fields have private set ---

    [Fact]
    public void AuditableAggregateRoot_CreatedBy_HasPrivateSet()
    {
        var prop = typeof(AuditableAggregateRoot<Guid>)
            .GetProperty(nameof(AuditableAggregateRoot<Guid>.CreatedBy));
        prop!.SetMethod!.IsPrivate.Should().BeTrue("CreatedBy setter must be private");
    }

    [Fact]
    public void AuditableAggregateRoot_CreatedOn_HasPrivateSet()
    {
        var prop = typeof(AuditableAggregateRoot<Guid>)
            .GetProperty(nameof(AuditableAggregateRoot<Guid>.CreatedOn));
        prop!.SetMethod!.IsPrivate.Should().BeTrue("CreatedOn setter must be private");
    }
}
