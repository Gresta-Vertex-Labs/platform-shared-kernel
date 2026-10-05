using FluentAssertions;
using SharedKernel.Domain.Aggregates;
using SharedKernel.Domain.Specifications;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Domain.Tests;

/// <summary>
/// F17 (wave 3b): <c>OnDelete</c> is an optional hook (virtual, no-op) on the soft-deletable bases, and
/// <c>ApplyThenBy</c> mirrors <c>ApplyOrderBy</c> (ascending, one parameter) with <c>ApplyThenByDescending</c>.
/// </summary>
public sealed class SoftDeleteAndOrderingSymmetryTests
{
    // No OnDelete/OnRestore override: compiles and deletes.
    private sealed class Customer(Guid id, IClock clock) : SoftDeletableAggregateRoot<Guid>(id, clock)
    {
        public void Close() => MarkAsDeleted("admin");
    }

    private sealed class Account(Guid id, IClock clock) : AuditableSoftDeletableAggregateRoot<Guid>(id, clock)
    {
        public void Close() => MarkAsDeleted("admin");
    }

    private sealed record Item(string Name, int Rank);

    private sealed class ByRankThenName : Specification<Item>
    {
        public ByRankThenName()
        {
            ApplyOrderBy(i => i.Rank);
            ApplyThenBy(i => i.Name);
        }
    }

    [Fact]
    public void SoftDeletableBases_WithoutAnOnDeleteOverride_StillSoftDelete()
    {
        var customer = new Customer(Guid.NewGuid(), new SystemClock());
        customer.Close();
        customer.IsDeleted.Should().BeTrue();
        customer.DomainEvents.Should().BeEmpty("the default OnDelete raises nothing");

        var account = new Account(Guid.NewGuid(), new SystemClock());
        account.Close();
        account.IsDeleted.Should().BeTrue();
        account.DeletedBy.Should().Be("admin");
    }

    [Fact]
    public void ApplyThenBy_IsAscending_LikeApplyOrderBy()
    {
        var spec = new ByRankThenName();

        spec.ThenBys.Should().ContainSingle();
        spec.ThenBys[0].Descending.Should().BeFalse();
    }
}
