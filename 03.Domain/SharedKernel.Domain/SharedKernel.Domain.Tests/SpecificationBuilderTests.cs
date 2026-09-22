using System.Linq.Expressions;
using FluentAssertions;
using SharedKernel.Domain.Specifications;

namespace SharedKernel.Domain.Tests;

/// <summary>
/// P-558 (D7): the inline builder, typed ThenInclude, projection specifications, ThenBy-without-OrderBy and
/// the And/Or/Not rules that never drop ordering or paging silently.
/// </summary>
public sealed class SpecificationBuilderTests
{
    private sealed class Supplier
    {
        public string Name { get; init; } = "";
    }

    private sealed class Product
    {
        public Supplier Supplier { get; init; } = new();
    }

    private sealed class Line
    {
        public Product Product { get; init; } = new();

        public bool Active { get; init; }
    }

    private sealed class Customer
    {
        public Address Address { get; init; } = new();
    }

    private sealed class Address
    {
        public string City { get; init; } = "";
    }

    private sealed class Order
    {
        public int Id { get; init; }

        public DateTimeOffset CreatedOn { get; init; }

        public bool Open { get; init; }

        public List<Line> Lines { get; init; } = [];

        public Customer Customer { get; init; } = new();
    }

    // ---- inline builder ----

    [Fact]
    public void For_BuildsCriteriaOrderingAndFlags()
    {
        ISpecification<Order> spec = Spec.For<Order>()
            .Where(o => o.Open)
            .Where(o => o.Id > 3)
            .OrderByDescending(o => o.CreatedOn)
            .ThenBy(o => o.Id)
            .Take(10)
            .Distinct()
            .AsSplitQuery()
            .IncludeDeleted();

        spec.Criteria!.Compile()(new Order { Open = true, Id = 4 }).Should().BeTrue();
        spec.Criteria!.Compile()(new Order { Open = true, Id = 3 }).Should().BeFalse();
        spec.OrderBy.Should().BeNull();
        spec.OrderByDescending.Should().NotBeNull();
        spec.ThenBys.Should().ContainSingle().Which.Descending.Should().BeFalse();
        spec.Take.Should().Be(10);
        spec.Skip.Should().BeNull();
        spec.IsDistinct.Should().BeTrue();
        spec.AsSplitQuery.Should().BeTrue();
        spec.IncludeDeleted.Should().BeTrue();
    }

    [Fact]
    public void For_Empty_MatchesEverything()
    {
        var spec = Spec.For<Order>();

        ((ISpecification<Order>)spec).Criteria.Should().BeNull();
        spec.IsSatisfiedBy(new Order()).Should().BeTrue();
    }

    [Fact]
    public void ValueTypedSortKey_IsBoxedIntoTheObjectSelector()
    {
        ISpecification<Order> spec = Spec.For<Order>().OrderBy(o => o.Id);

        spec.OrderBy!.Body.NodeType.Should().Be(ExpressionType.Convert);
        spec.OrderBy.Compile()(new Order { Id = 7 }).Should().Be(7);
    }

    [Fact]
    public void ThenBy_WithoutOrderBy_Throws()
    {
        var act = () => Spec.For<Order>().ThenBy(o => o.Id);

        act.Should().Throw<InvalidOperationException>().WithMessage("*no primary sort*");
    }

    [Fact]
    public void SubclassThenBy_WithoutOrderBy_Throws()
    {
        FluentActions.Invoking(() => new ThenByFirst()).Should().Throw<InvalidOperationException>();
    }

    private sealed class ThenByFirst : Specification<Order>
    {
        public ThenByFirst() => ApplyThenBy(o => o.Id);
    }

    [Fact]
    public void SecondPrimarySort_Throws()
    {
        var act = () => Spec.For<Order>().OrderBy(o => o.Id).OrderByDescending(o => o.CreatedOn);

        act.Should().Throw<InvalidOperationException>();
    }

    // ---- typed ThenInclude ----

    [Fact]
    public void ThenInclude_ThroughCollection_RecordsTheFullPath()
    {
        ISpecification<Order> spec = Spec.For<Order>()
            .Include(o => o.Lines).ThenInclude(l => l.Product).ThenInclude(p => p.Supplier)
            .OrderBy(o => o.Id);

        spec.Includes.Should().ContainSingle();
        spec.StringIncludes.Should().Equal("Lines.Product", "Lines.Product.Supplier");
        spec.OrderBy.Should().NotBeNull();
    }

    [Fact]
    public void ThenInclude_ThroughReference_RecordsTheFullPath()
    {
        ISpecification<Order> spec = Spec.For<Order>().Include(o => o.Customer).ThenInclude(c => c.Address);

        spec.StringIncludes.Should().Equal("Customer.Address");
    }

    [Fact]
    public void ThenInclude_BranchesFromTheSameInclude_KeepBothPaths()
    {
        var builder = Spec.For<Order>();
        var lines = builder.Include(o => o.Lines);
        lines.ThenInclude(l => l.Product);
        lines.ThenInclude(l => l.Product).ThenInclude(p => p.Supplier);

        ((ISpecification<Order>)builder).StringIncludes
            .Should().Equal("Lines.Product", "Lines.Product.Supplier");
    }

    [Fact]
    public void ThenInclude_AfterFilteredInclude_Throws()
    {
        var act = () => Spec.For<Order>().Include(o => o.Lines.Where(l => l.Active)).ThenInclude(l => l.Product);

        act.Should().Throw<NotSupportedException>().WithMessage("*filtered include*");
    }

    [Fact]
    public void ThenInclude_WithNonMemberSelector_Throws()
    {
        var act = () => Spec.For<Order>().Include(o => o.Customer).ThenInclude(c => c.Address.City.Trim());

        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void SubclassStyle_AddInclude_ThenInclude_Works()
    {
        var spec = new OrdersWithProducts();

        spec.Includes.Should().ContainSingle();
        spec.StringIncludes.Should().Equal("Lines.Product");
    }

    private sealed class OrdersWithProducts : Specification<Order>
    {
        public OrdersWithProducts() => AddInclude(o => o.Lines).ThenInclude(l => l.Product);
    }

    // ---- projections ----

    [Fact]
    public void Select_SharesTheBuilderShape()
    {
        var spec = Spec.For<Order>().Where(o => o.Open).OrderBy(o => o.Id).Select(o => new { o.Id });

        spec.Criteria.Should().NotBeNull();
        spec.OrderBy.Should().NotBeNull();
        spec.Selector.Compile()(new Order { Id = 5 }).Id.Should().Be(5);
    }

    [Fact]
    public void ProjectionSpecification_WithoutSelector_Throws()
    {
        var act = () => new NoSelector().Selector;

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ProjectionSpecification_SecondSelector_Throws() =>
        FluentActions.Invoking(() => new TwoSelectors()).Should().Throw<InvalidOperationException>();

    [Fact]
    public void ProjectionSpecification_ExposesItsSelector() =>
        new OrderIds().Selector.Compile()(new Order { Id = 9 }).Should().Be(9);

    private sealed class NoSelector : ProjectionSpecification<Order, int>;

    private sealed class OrderIds : ProjectionSpecification<Order, int>
    {
        public OrderIds() => ApplySelector(o => o.Id);
    }

    private sealed class TwoSelectors : ProjectionSpecification<Order, int>
    {
        public TwoSelectors()
        {
            ApplySelector(o => o.Id);
            ApplySelector(o => o.Id + 1);
        }
    }

    // ---- composition never drops ordering or paging silently ----

    [Fact]
    public void And_CarriesTheSingleOperandOrdering()
    {
        var ordered = Spec.For<Order>().OrderByDescending(o => o.CreatedOn).ThenBy(o => o.Id);
        var filter = Spec.For<Order>().Where(o => o.Open);

        var and = filter.And(ordered);

        and.OrderByDescending.Should().NotBeNull();
        and.ThenBys.Should().ContainSingle();
    }

    [Fact]
    public void Or_CarriesTheSingleOperandOrdering()
    {
        var ordered = Spec.For<Order>().Where(o => o.Id > 1).OrderBy(o => o.Id);

        var or = ordered.Or(Spec.For<Order>().Where(o => o.Open));

        or.OrderBy.Should().NotBeNull();
    }

    [Fact]
    public void Not_CarriesTheOrdering()
    {
        var not = Spec.For<Order>().Where(o => o.Open).OrderBy(o => o.Id).Not();

        not.OrderBy.Should().NotBeNull();
    }

    [Fact]
    public void And_BothOrdered_Throws()
    {
        var left = Spec.For<Order>().OrderBy(o => o.Id);
        var right = Spec.For<Order>().OrderBy(o => o.CreatedOn);

        FluentActions.Invoking(() => left.And(right)).Should().Throw<InvalidOperationException>()
            .WithMessage("*primary sort*");
        FluentActions.Invoking(() => left.Or(right)).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Composition_WithPaging_Throws()
    {
        var paged = Spec.For<Order>().OrderBy(o => o.Id).Take(5);
        var filter = Spec.For<Order>().Where(o => o.Open);

        FluentActions.Invoking(() => filter.And(paged)).Should().Throw<InvalidOperationException>()
            .WithMessage("*Skip/Take*");
        FluentActions.Invoking(() => paged.Or(filter)).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => paged.Not()).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Composition_OrsTheDistinctFlag()
    {
        var and = Spec.For<Order>().Distinct().And(Spec.For<Order>().Where(o => o.Open));

        and.IsDistinct.Should().BeTrue();
    }
}
