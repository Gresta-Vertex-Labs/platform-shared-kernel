using FluentAssertions;
using SharedKernel.Domain.Aggregates;
using SharedKernel.Domain.Events;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Domain.Tests;

/// <summary>P-558: Restore() on the soft-deletable aggregate bases replaces the deleted IRestorableRepository.</summary>
public sealed class SoftDeleteRestoreTests
{
    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;

        public DateOnly Today => DateOnly.FromDateTime(now.UtcDateTime);
    }

    private sealed record Closed : DomainEvent;

    private sealed record Reopened : DomainEvent;

    private sealed class Customer(Guid id, IClock clock) : SoftDeletableAggregateRoot<Guid>(id, clock)
    {
        public void Close() => MarkAsDeleted("admin");

        public void Reopen() => Restore();

        protected override void OnDelete() => RaiseDomainEvent(at => new Closed { OccurredOn = at });

        protected override void OnRestore() => RaiseDomainEvent(at => new Reopened { OccurredOn = at });
    }

    private sealed class Account(Guid id, IClock clock) : FullAuditableAggregateRoot<Guid>(id, clock)
    {
        public void Close() => MarkAsDeleted("admin");

        public void Reopen() => Restore();

        protected override void OnDelete()
        {
        }
    }

    private static readonly IClock Clock = new FixedClock(new DateTimeOffset(2026, 9, 21, 10, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Restore_ClearsDeletedState_AndRaisesTheRestoreEvent()
    {
        var customer = new Customer(Guid.NewGuid(), Clock);
        customer.Close();

        customer.Reopen();

        customer.IsDeleted.Should().BeFalse();
        customer.DeletedOn.Should().BeNull();
        customer.DeletedBy.Should().BeNull();
        customer.DomainEvents.Should().HaveCount(2);
        customer.DomainEvents.Last().Should().BeOfType<Reopened>();
    }

    [Fact]
    public void Restore_WhenNotDeleted_IsANoOp()
    {
        var customer = new Customer(Guid.NewGuid(), Clock);

        customer.Reopen();

        customer.IsDeleted.Should().BeFalse();
        customer.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Restore_OnAuditableBase_ClearsDeletedState_WithoutRequiringAnOverride()
    {
        var account = new Account(Guid.NewGuid(), Clock);
        account.Close();

        account.Reopen();

        account.IsDeleted.Should().BeFalse();
        account.DeletedOn.Should().BeNull();
        account.DeletedBy.Should().BeNull();
    }
}
