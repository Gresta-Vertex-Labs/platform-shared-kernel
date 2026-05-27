using FluentAssertions;
using SharedKernel.Domain.Specifications;

namespace SharedKernel.Domain.Tests;

public class SpecificationCompositionTests
{
    // --- Test double domain type ---

    private sealed record Product(string Name, decimal Price, bool IsActive);

    // --- Concrete specification implementations ---

    private sealed class ActiveProductSpec : Specification<Product>
    {
        public ActiveProductSpec() => AddCriteria(p => p.IsActive);
    }

    private sealed class AffordableProductSpec : Specification<Product>
    {
        public AffordableProductSpec(decimal maxPrice) =>
            AddCriteria(p => p.Price <= maxPrice);
    }

    private sealed class AllProductsSpec : Specification<Product>
    {
        // No criteria — matches everything
    }

    private static readonly List<Product> Products =
    [
        new("Widget", 10m, true),
        new("Gadget", 50m, true),
        new("Gizmo", 100m, false),
        new("Doohickey", 5m, false),
    ];

    private static IEnumerable<Product> Apply(ISpecification<Product> spec, IEnumerable<Product> source)
    {
        var query = source.AsQueryable();
        if (spec.Criteria is not null)
            query = query.Where(spec.Criteria);
        return query.ToList();
    }

    // --- AndSpecification ---

    [Fact]
    public void And_ActiveAndAffordable_ReturnsOnlyActiveAndCheap()
    {
        var spec = new ActiveProductSpec().And(new AffordableProductSpec(20m));
        var result = Apply(spec, Products);

        result.Should().ContainSingle(p => p.Name == "Widget");
        result.Should().NotContain(p => p.Name == "Gadget"); // too expensive
        result.Should().NotContain(p => p.Name == "Gizmo");  // not active
    }

    [Fact]
    public void And_LeftNoCriteria_UsesRightCriteria()
    {
        var spec = new AllProductsSpec().And(new ActiveProductSpec());
        var result = Apply(spec, Products);

        result.Should().HaveCount(2); // Widget and Gadget are active
    }

    [Fact]
    public void And_RightNoCriteria_UsesLeftCriteria()
    {
        var spec = new ActiveProductSpec().And(new AllProductsSpec());
        var result = Apply(spec, Products);

        result.Should().HaveCount(2);
    }

    [Fact]
    public void And_BothNoCriteria_NoCriteria()
    {
        var spec = new AllProductsSpec().And(new AllProductsSpec());
        spec.Criteria.Should().BeNull();
    }

    // --- OrSpecification ---

    [Fact]
    public void Or_ActiveOrAffordable_ReturnsBothGroups()
    {
        var spec = new ActiveProductSpec().Or(new AffordableProductSpec(5m));
        var result = Apply(spec, Products);

        // Widget: active AND cheap; Gadget: active; Doohickey: cheap (5 <= 5) but not active
        result.Should().Contain(p => p.Name == "Widget");
        result.Should().Contain(p => p.Name == "Gadget");
        result.Should().Contain(p => p.Name == "Doohickey");
        result.Should().NotContain(p => p.Name == "Gizmo"); // not active and price 100 > 5
    }

    // --- NotSpecification ---

    [Fact]
    public void Not_ActiveSpec_ReturnsInactiveProducts()
    {
        var spec = new ActiveProductSpec().Not();
        var result = Apply(spec, Products);

        result.Should().HaveCount(2);
        result.Should().AllSatisfy(p => p.IsActive.Should().BeFalse());
    }

    [Fact]
    public void Not_NoCriteriaSpec_HasNullCriteria()
    {
        var spec = new AllProductsSpec().Not();
        spec.Criteria.Should().BeNull();
    }

    // --- Extension methods produce correct types ---

    [Fact]
    public void Extension_And_ProducesAndSpecification()
    {
        var result = new ActiveProductSpec().And(new AffordableProductSpec(10m));
        result.Should().BeOfType<AndSpecification<Product>>();
    }

    [Fact]
    public void Extension_Or_ProducesOrSpecification()
    {
        var result = new ActiveProductSpec().Or(new AffordableProductSpec(10m));
        result.Should().BeOfType<OrSpecification<Product>>();
    }

    [Fact]
    public void Extension_Not_ProducesNotSpecification()
    {
        var result = new ActiveProductSpec().Not();
        result.Should().BeOfType<NotSpecification<Product>>();
    }

    // --- Specification builder methods ---

    private sealed class PagedActiveSpec : Specification<Product>
    {
        public PagedActiveSpec()
        {
            AddCriteria(p => p.IsActive);
            ApplyOrderBy(p => p.Name);
            ApplyPaging(skip: 0, take: 10);
            ApplyDistinct();
            AddInclude(p => p.Name); // include expression (not meaningful here, just tests the method)
        }
    }

    [Fact]
    public void Specification_BuilderMethods_SetPropertiesCorrectly()
    {
        var spec = new PagedActiveSpec();

        spec.Criteria.Should().NotBeNull();
        spec.OrderBy.Should().NotBeNull();
        spec.Skip.Should().Be(0);
        spec.Take.Should().Be(10);
        spec.IsDistinct.Should().BeTrue();
        spec.Includes.Should().HaveCount(1);
    }

    private sealed class DescOrderSpec : Specification<Product>
    {
        public DescOrderSpec()
        {
            ApplyOrderByDescending(p => p.Price);
            ApplyThenBy(p => p.Name, descending: false);
        }
    }

    [Fact]
    public void Specification_OrderByDescending_SetCorrectly()
    {
        var spec = new DescOrderSpec();

        spec.OrderByDescending.Should().NotBeNull();
        spec.ThenBys.Should().HaveCount(1);
        spec.ThenBys[0].Descending.Should().BeFalse();
    }

    // --- T-13: IsSatisfiedBy (P-038/WO-009) ---

    [Fact]
    public void IsSatisfiedBy_CriteriaLessSpec_ReturnsTrue()
    {
        var spec = new AllProductsSpec();
        var product = new Product("Widget", 10m, true);

        spec.IsSatisfiedBy(product).Should().BeTrue("criteria-less specification matches all entities");
    }

    [Fact]
    public void IsSatisfiedBy_MatchingEntity_ReturnsTrue()
    {
        var spec = new ActiveProductSpec();
        var active = new Product("Widget", 10m, true);

        spec.IsSatisfiedBy(active).Should().BeTrue();
    }

    [Fact]
    public void IsSatisfiedBy_NonMatchingEntity_ReturnsFalse()
    {
        var spec = new ActiveProductSpec();
        var inactive = new Product("Gizmo", 100m, false);

        spec.IsSatisfiedBy(inactive).Should().BeFalse();
    }

    [Fact]
    public void IsSatisfiedBy_CalledMultipleTimes_UsesCachedDelegate()
    {
        // We use reflection to verify the private _compiledCriteria field is populated after first call
        // and remains the same reference on subsequent calls.
        var spec = new ActiveProductSpec();
        var product = new Product("Widget", 10m, true);

        spec.IsSatisfiedBy(product);

        var field = typeof(Specification<Product>)
            .GetField("_compiledCriteria", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var delegate1 = field!.GetValue(spec);

        spec.IsSatisfiedBy(product);
        var delegate2 = field.GetValue(spec);

        delegate1.Should().BeSameAs(delegate2, "compiled delegate must be cached after first call");
    }

    // --- T-15: AsNoTracking (P-040/WO-009) ---

    private sealed class NoTrackingSpec : Specification<Product>
    {
        public NoTrackingSpec()
        {
            AddCriteria(p => p.IsActive);
            ApplyNoTracking();
        }
    }

    private sealed class ReadOnlyActiveSpec : ReadOnlySpecification<Product>
    {
        public ReadOnlyActiveSpec() => AddCriteria(p => p.IsActive);
    }

    [Fact]
    public void Specification_AsNoTracking_DefaultIsFalse()
    {
        var spec = new ActiveProductSpec();
        spec.AsNoTracking.Should().BeFalse();
    }

    [Fact]
    public void Specification_ApplyNoTracking_SetsTrue()
    {
        var spec = new NoTrackingSpec();
        spec.AsNoTracking.Should().BeTrue();
    }

    [Fact]
    public void ReadOnlySpecification_AsNoTracking_AlwaysTrue()
    {
        var spec = new ReadOnlyActiveSpec();
        spec.AsNoTracking.Should().BeTrue();
    }

    [Fact]
    public void AndSpecification_AsNoTracking_TrueWhenEitherOperandIsTrue()
    {
        var noTrack = new NoTrackingSpec();
        var tracking = new ActiveProductSpec();

        var and1 = noTrack.And(tracking);
        var and2 = tracking.And(noTrack);
        var and3 = tracking.And(tracking);

        and1.AsNoTracking.Should().BeTrue();
        and2.AsNoTracking.Should().BeTrue();
        and3.AsNoTracking.Should().BeFalse();
    }

    [Fact]
    public void OrSpecification_AsNoTracking_TrueWhenEitherOperandIsTrue()
    {
        var noTrack = new NoTrackingSpec();
        var tracking = new ActiveProductSpec();

        var or1 = noTrack.Or(tracking);
        var or2 = tracking.Or(noTrack);
        var or3 = tracking.Or(tracking);

        or1.AsNoTracking.Should().BeTrue();
        or2.AsNoTracking.Should().BeTrue();
        or3.AsNoTracking.Should().BeFalse();
    }

    [Fact]
    public void NotSpecification_AsNoTracking_TrueWhenOperandIsTrue()
    {
        var noTrack = new NoTrackingSpec();
        var tracking = new ActiveProductSpec();

        var not1 = noTrack.Not();
        var not2 = tracking.Not();

        not1.AsNoTracking.Should().BeTrue();
        not2.AsNoTracking.Should().BeFalse();
    }
}
