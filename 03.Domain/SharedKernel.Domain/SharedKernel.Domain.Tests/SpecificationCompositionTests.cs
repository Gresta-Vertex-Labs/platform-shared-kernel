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
    public void Not_NoCriteriaSpec_MatchesNothing()
    {
        // A spec without criteria matches everything, so its negation must match nothing, not everything.
        var spec = new AllProductsSpec().Not();
        spec.Criteria.Should().NotBeNull();
        spec.IsSatisfiedBy(new Product("any", 1m, true)).Should().BeFalse();
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

    // --- T-31: P-307/WO-051 — Includes/StringIncludes composite propagation ---

    private sealed class ProductWithIncludeSpec : Specification<Product>
    {
        public ProductWithIncludeSpec()
        {
            AddCriteria(p => p.IsActive);
            AddInclude(p => p.Name);
            AddStringInclude("Category.Parent");
        }
    }

    private sealed class ProductNoIncludeSpec : Specification<Product>
    {
        public ProductNoIncludeSpec() => AddCriteria(p => p.Price > 0);
    }

    [Fact]
    public void AndSpecification_Includes_UnionedFromBothOperands()
    {
        var withInclude = new ProductWithIncludeSpec();
        var without = new ProductNoIncludeSpec();

        var spec = withInclude.And(without);

        spec.Includes.Should().HaveCount(1);
        spec.StringIncludes.Should().ContainSingle(p => p == "Category.Parent");
    }

    [Fact]
    public void AndSpecification_Includes_UnionedRegardlessOfOperandOrder()
    {
        var withInclude = new ProductWithIncludeSpec();
        var without = new ProductNoIncludeSpec();

        var spec = without.And(withInclude);

        spec.Includes.Should().HaveCount(1);
        spec.StringIncludes.Should().ContainSingle(p => p == "Category.Parent");
    }

    [Fact]
    public void OrSpecification_Includes_UnionedFromBothOperands()
    {
        var withInclude = new ProductWithIncludeSpec();
        var without = new ProductNoIncludeSpec();

        var spec = withInclude.Or(without);

        spec.Includes.Should().HaveCount(1);
        spec.StringIncludes.Should().ContainSingle(p => p == "Category.Parent");
    }

    [Fact]
    public void NotSpecification_Includes_UnionedFromOperand()
    {
        var withInclude = new ProductWithIncludeSpec();

        var spec = withInclude.Not();

        spec.Includes.Should().HaveCount(1);
        spec.StringIncludes.Should().ContainSingle(p => p == "Category.Parent");
    }

    [Fact]
    public void NotSpecification_NoIncludeOperand_ProducesNoIncludes()
    {
        var without = new ProductNoIncludeSpec();

        var spec = without.Not();

        spec.Includes.Should().BeEmpty();
        spec.StringIncludes.Should().BeEmpty();
    }

    // --- T-39: P-313/WO-051 — Specification<T>.Create(criteria) ad hoc factory ---

    [Fact]
    public void Create_MatchesEquivalentNamedSpecification_IsSatisfiedByBehavior()
    {
        var adHoc = Specification<Product>.Create(p => p.IsActive);
        var named = new ActiveProductSpec();

        var active = new Product("Widget", 10m, true);
        var inactive = new Product("Gizmo", 100m, false);

        adHoc.IsSatisfiedBy(active).Should().Be(named.IsSatisfiedBy(active));
        adHoc.IsSatisfiedBy(inactive).Should().Be(named.IsSatisfiedBy(inactive));
    }

    [Fact]
    public void Create_ComposesWithAnotherAdHocSpec_ViaAnd()
    {
        var spec = Specification<Product>.Create(p => p.IsActive)
            .And(Specification<Product>.Create(p => p.Price <= 20m));

        var result = Apply(spec, Products);

        result.Should().ContainSingle(p => p.Name == "Widget");
    }

    [Fact]
    public void Create_ComposesWithNamedSpecification_ViaOr()
    {
        var spec = Specification<Product>.Create(p => p.Name == "Doohickey")
            .Or(new ActiveProductSpec());

        var result = Apply(spec, Products);

        result.Should().Contain(p => p.Name == "Widget");
        result.Should().Contain(p => p.Name == "Gadget");
        result.Should().Contain(p => p.Name == "Doohickey");
    }

    [Fact]
    public void Create_ComposesViaNot()
    {
        var spec = Specification<Product>.Create(p => p.IsActive).Not();

        var result = Apply(spec, Products);

        result.Should().HaveCount(2);
        result.Should().AllSatisfy(p => p.IsActive.Should().BeFalse());
    }

    [Fact]
    public void Create_NonCriteriaMembers_DefaultToEmptyBaseline()
    {
        var spec = Specification<Product>.Create(p => p.IsActive);

        spec.Includes.Should().BeEmpty();
        spec.StringIncludes.Should().BeEmpty();
        spec.OrderBy.Should().BeNull();
        spec.OrderByDescending.Should().BeNull();
        spec.ThenBys.Should().BeEmpty();
        spec.Skip.Should().BeNull();
        spec.Take.Should().BeNull();
        spec.IsDistinct.Should().BeFalse();
        spec.IncludeDeleted.Should().BeFalse();
        spec.AsSplitQuery.Should().BeFalse();
    }
}
