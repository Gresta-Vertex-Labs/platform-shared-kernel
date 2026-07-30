using FluentAssertions;
using SharedKernel.Domain.Specifications;

namespace SharedKernel.Domain.Tests;

/// <summary>
/// T-32: P-308a/WO-051 — KeysetSpecification&lt;T, TKey&gt; shape-correctness tests.
/// </summary>
public class KeysetSpecificationTests
{
    private sealed record Order(Guid Id, DateTimeOffset CreatedOn, bool IsActive);

    private sealed class ActiveOrdersKeysetSpec : KeysetSpecification<Order, DateTimeOffset>
    {
        public ActiveOrdersKeysetSpec(DateTimeOffset? afterKey, object? afterId, bool descending, int take)
            : base(o => o.CreatedOn, o => o.Id, afterKey, afterId, descending, take)
        {
            AddCriteria(o => o.IsActive);
        }
    }

    // --- First page (no cursor) ---

    [Fact]
    public void FirstPage_NoCursor_AfterKeyAndAfterId_AreNull()
    {
        var spec = new ActiveOrdersKeysetSpec(afterKey: null, afterId: null, descending: false, take: 20);

        spec.AfterKey.Should().BeNull();
        spec.AfterId.Should().BeNull();
    }

    // --- Ordering: primary sort + mandatory Id tiebreaker ---

    [Fact]
    public void OrderBy_Ascending_SetCorrectly()
    {
        var spec = new ActiveOrdersKeysetSpec(null, null, descending: false, take: 10);

        spec.OrderBy.Should().NotBeNull();
        spec.OrderByDescending.Should().BeNull();
    }

    [Fact]
    public void OrderByDescending_WhenDescendingTrue_SetCorrectly()
    {
        var spec = new ActiveOrdersKeysetSpec(null, null, descending: true, take: 10);

        spec.OrderByDescending.Should().NotBeNull();
        spec.OrderBy.Should().BeNull();
    }

    [Fact]
    public void ThenBys_ContainsMandatoryIdTiebreaker()
    {
        var spec = new ActiveOrdersKeysetSpec(null, null, descending: false, take: 10);

        spec.ThenBys.Should().ContainSingle();
        spec.ThenBys[0].Descending.Should().BeFalse();
    }

    [Fact]
    public void Descending_PropertyReflectsConstructorArgument()
    {
        var descSpec = new ActiveOrdersKeysetSpec(null, null, descending: true, take: 10);
        var ascSpec = new ActiveOrdersKeysetSpec(null, null, descending: false, take: 10);

        descSpec.Descending.Should().BeTrue();
        ascSpec.Descending.Should().BeFalse();
    }

    // --- Paging: Skip always 0, Take reflects caller value ---

    [Fact]
    public void Paging_SkipAlwaysZero_TakeReflectsArgument()
    {
        var spec = new ActiveOrdersKeysetSpec(null, null, descending: false, take: 25);

        spec.Skip.Should().Be(0);
        spec.Take.Should().Be(25);
    }

    // --- Cursor values readable when supplied ---

    [Fact]
    public void SubsequentPage_CursorValues_AreReadableAndCorrect()
    {
        var afterKey = DateTimeOffset.UtcNow;
        var afterId = Guid.NewGuid();

        var spec = new ActiveOrdersKeysetSpec(afterKey, afterId, descending: false, take: 10);

        spec.AfterKey.Should().Be(afterKey);
        spec.AfterId.Should().Be(afterId);
    }

    // --- Guards ---

    [Fact]
    public void Constructor_TakeLessThanOne_ThrowsArgumentOutOfRangeException()
    {
        var act = () => new ActiveOrdersKeysetSpec(null, null, descending: false, take: 0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_NegativeTake_ThrowsArgumentOutOfRangeException()
    {
        var act = () => new ActiveOrdersKeysetSpec(null, null, descending: false, take: -1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_OnlyAfterKeySupplied_ThrowsArgumentException()
    {
        var act = () => new ActiveOrdersKeysetSpec(DateTimeOffset.UtcNow, afterId: null, descending: false, take: 10);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_OnlyAfterIdSupplied_ThrowsArgumentException()
    {
        var act = () => new ActiveOrdersKeysetSpec(afterKey: null, afterId: Guid.NewGuid(), descending: false, take: 10);

        act.Should().Throw<ArgumentException>();
    }

    // --- ReadOnlySpecification<T> inheritance ---

    [Fact]
    public void KeysetSpecification_AsNoTracking_AlwaysTrue()
    {
        var spec = new ActiveOrdersKeysetSpec(null, null, descending: false, take: 10);

        spec.AsNoTracking.Should().BeTrue("KeysetSpecification<T,TKey> extends ReadOnlySpecification<T>");
    }

    // --- Criteria still composes normally ---

    [Fact]
    public void Criteria_StillAppliedFromSubclassConstructor()
    {
        var spec = new ActiveOrdersKeysetSpec(null, null, descending: false, take: 10);

        var active = new Order(Guid.NewGuid(), DateTimeOffset.UtcNow, true);
        var inactive = new Order(Guid.NewGuid(), DateTimeOffset.UtcNow, false);

        spec.IsSatisfiedBy(active).Should().BeTrue();
        spec.IsSatisfiedBy(inactive).Should().BeFalse();
    }
}
