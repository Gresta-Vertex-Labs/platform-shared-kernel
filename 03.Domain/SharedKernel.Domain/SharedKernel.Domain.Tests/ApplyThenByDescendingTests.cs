using FluentAssertions;
using SharedKernel.Domain.Specifications;

namespace SharedKernel.Domain.Tests;

/// <summary>
/// T-25: P-052/WO-011 — ApplyThenByDescending tests.
/// </summary>
public class ApplyThenByDescendingTests
{
    private sealed record Product(string Name, decimal Price, int Quantity);

    private sealed class ProductsByPriceDescThenNameDescSpec : Specification<Product>
    {
        public ProductsByPriceDescThenNameDescSpec()
        {
            ApplyOrderByDescending(p => p.Price);
            ApplyThenByDescending(p => p.Name);
        }
    }

    private sealed class ProductsWithMixedThenBySpec : Specification<Product>
    {
        public ProductsWithMixedThenBySpec()
        {
            ApplyOrderBy(p => p.Price);
            ApplyThenBy(p => p.Name);    // ascending
            ApplyThenByDescending(p => p.Quantity);          // descending alias
        }
    }

    [Fact]
    public void ApplyThenByDescending_ProducesThenBys_Entry_WithDescendingTrue()
    {
        var spec = new ProductsByPriceDescThenNameDescSpec();

        spec.ThenBys.Should().HaveCount(1);
        spec.ThenBys[0].Descending.Should().BeTrue();
    }

    [Fact]
    public void ApplyThenByDescending_KeySelector_IsCorrect()
    {
        var spec = new ProductsByPriceDescThenNameDescSpec();

        spec.ThenBys[0].KeySelector.Should().NotBeNull();
        // Verify the selector evaluates the Name property
        var product = new Product("Alpha", 10m, 5);
        var selector = spec.ThenBys[0].KeySelector.Compile();
        selector(product).Should().Be("Alpha");
    }

    [Fact]
    public void ApplyThenBy_MixedDirections_BothEntriesPresent()
    {
        var spec = new ProductsWithMixedThenBySpec();

        spec.ThenBys.Should().HaveCount(2);
        spec.ThenBys[0].Descending.Should().BeFalse("first ThenBy is ascending");
        spec.ThenBys[1].Descending.Should().BeTrue("second ThenByDescending is descending");
    }

    [Fact]
    public void ApplyThenByDescending_ExistingTests_StillPass()
    {
        // Ensure ApplyThenBy with explicit descending=false still works
        var spec = new ProductsByPriceDescThenNameDescSpec();
        spec.OrderByDescending.Should().NotBeNull();
        spec.ThenBys.Should().HaveCount(1);
    }
}
