using System.Linq.Expressions;
using SharedKernel.Domain.Aggregates;
using SharedKernel.Domain.Specifications;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Persistence;

namespace SharedKernel.Testing.SelfTests.Persistence;

// Shared fixtures for FakeRepository<TAggregate,TId>/FakeUnitOfWork/FakePersistenceTransaction
// self-tests (T-66..T-71, P-335/WO-053) — none are part of the testing-infrastructure public
// surface; they exist purely to drive those types' tests.
//
// TestSoftDeletableOrder is deliberately a DIFFERENT type from Persistence/TestFixtures.cs' own
// TestOrder (a plain, non-soft-deletable AggregateRoot<Guid> used by the EF Core-backed fixtures
// in this same namespace) — FakeRepository's write-side "always a hard removal, even for
// ISoftDeletable" behavior specifically needs a genuine ISoftDeletable aggregate to exercise,
// mirroring AggregateRootFakerTests' own established per-file fixture convention in this project.

/// <summary>Minimal soft-deletable aggregate root used only by the FakeRepository/FakeUnitOfWork self-tests.</summary>
public sealed class TestSoftDeletableOrder : SoftDeletableAggregateRoot<Guid>
{
    public TestSoftDeletableOrder(Guid id, string customer, decimal total, int rank, IClock clock)
        : base(id, clock)
    {
        Customer = customer;
        Total = total;
        Rank = rank;
    }

    private TestSoftDeletableOrder() { }

    public string Customer { get; private set; } = string.Empty;

    public decimal Total { get; private set; }

    /// <summary>A value-typed, struct-constrained sort key used by the ordering/keyset pipeline tests.</summary>
    public int Rank { get; private set; }

    protected override void OnDelete() { }

    /// <summary>Test-only helper mirroring the documented <see cref="SoftDeletableAggregateRoot{TId}"/> usage example.</summary>
    public void Delete(string deletedBy = "test-user") => MarkAsDeleted(deletedBy);
}

/// <summary>Bogus faker for <see cref="TestSoftDeletableOrder"/>, mirroring TestFixtures.cs' own TestOrderFaker.</summary>
public sealed class TestSoftDeletableOrderFaker : AggregateRootFaker<TestSoftDeletableOrder, Guid>
{
    public TestSoftDeletableOrderFaker()
    {
        CustomInstantiator(f => new TestSoftDeletableOrder(
            f.Random.Guid(), f.Person.FullName, f.Random.Decimal(1, 1000), f.Random.Int(0, 1000), new FakeClock()));
    }
}

/// <summary>Criteria-less specification; optionally soft-delete-inclusive, for round-trip proofs.</summary>
public sealed class AllTestOrdersSpecification : Specification<TestSoftDeletableOrder>
{
    public AllTestOrdersSpecification(bool includeDeleted = false)
    {
        if (includeDeleted)
            IncludeSoftDeleted();
    }
}

/// <summary>Filters by an exact customer name.</summary>
public sealed class TestOrdersByCustomerSpecification : Specification<TestSoftDeletableOrder>
{
    public TestOrdersByCustomerSpecification(string customer) => AddCriteria(o => o.Customer == customer);
}

/// <summary>Orders by Rank (primary) then Customer (secondary) — proves OrderBy/ThenBy precedence.</summary>
public sealed class TestOrdersOrderedByRankThenCustomerSpecification : Specification<TestSoftDeletableOrder>
{
    public TestOrdersOrderedByRankThenCustomerSpecification(bool descending = false)
    {
        if (descending)
            ApplyOrderByDescending(o => o.Rank);
        else
            ApplyOrderBy(o => o.Rank);

        ApplyThenBy(o => o.Customer);
    }
}

/// <summary>Criteria-less, ordering-less specification marked distinct only.</summary>
public sealed class TestOrdersDistinctSpecification : Specification<TestSoftDeletableOrder>
{
    public TestOrdersDistinctSpecification() => ApplyDistinct();
}

/// <summary>Ordered by Rank with an explicit Skip/Take window — the paging workhorse spec.</summary>
public sealed class TestOrdersPagedSpecification : Specification<TestSoftDeletableOrder>
{
    public TestOrdersPagedSpecification(int skip, int take)
    {
        ApplyOrderBy(o => o.Rank);
        ApplyPaging(skip, take);
    }
}

/// <summary>Ordered by Rank with NO Take set — exercises the "no Take = one full page" derivation.</summary>
public sealed class TestOrdersUnpagedOrderedSpecification : Specification<TestSoftDeletableOrder>
{
    public TestOrdersUnpagedOrderedSpecification() => ApplyOrderBy(o => o.Rank);
}

/// <summary>A paged projection specification (ordered by Rank) projecting to the Customer name.</summary>
public sealed class TestOrdersPagedCustomerProjectionSpecification
    : Specification<TestSoftDeletableOrder>, IProjectionSpecification<TestSoftDeletableOrder, string>
{
    public TestOrdersPagedCustomerProjectionSpecification(int skip, int take)
    {
        ApplyOrderBy(o => o.Rank);
        ApplyPaging(skip, take);
        Selector = o => o.Customer;
    }

    public Expression<Func<TestSoftDeletableOrder, string>> Selector { get; }
}

/// <summary>Optional-criteria projection specification with no paging — for match/no-match proofs.</summary>
public sealed class TestOrdersCustomerProjectionSpecification
    : Specification<TestSoftDeletableOrder>, IProjectionSpecification<TestSoftDeletableOrder, string>
{
    public TestOrdersCustomerProjectionSpecification(Expression<Func<TestSoftDeletableOrder, bool>> criteria)
    {
        AddCriteria(criteria);
        Selector = o => o.Customer;
    }

    public Expression<Func<TestSoftDeletableOrder, string>> Selector { get; }
}

