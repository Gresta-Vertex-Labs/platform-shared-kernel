using FluentAssertions;
using SharedKernel.Domain.Specifications;

namespace SharedKernel.Domain.Tests;

/// <summary>
/// T-23: P-050/WO-011 — AllSpecification and EmptySpecification sentinel tests.
/// </summary>
public class SentinelSpecificationTests
{
    private sealed record Widget(string Name, bool IsActive);

    private sealed class ActiveWidgetSpec : Specification<Widget>
    {
        public ActiveWidgetSpec() => AddCriteria(w => w.IsActive);
    }

    private static readonly List<Widget> Widgets =
    [
        new("Alpha", true),
        new("Beta", false),
    ];

    private static IEnumerable<Widget> Apply(Specification<Widget> spec, IEnumerable<Widget> source)
    {
        var query = source.AsQueryable();
        if (spec.Criteria is not null)
            query = query.Where(spec.Criteria);
        return query.ToList();
    }

    // --- AllSpecification ---

    [Fact]
    public void AllSpecification_Criteria_IsNull()
    {
        var spec = new AllSpecification<Widget>();
        spec.Criteria.Should().BeNull();
    }

    [Fact]
    public void AllSpecification_IsSatisfiedBy_ReturnsTrue_ForAnyEntity()
    {
        var spec = new AllSpecification<Widget>();
        spec.IsSatisfiedBy(new Widget("X", true)).Should().BeTrue();
        spec.IsSatisfiedBy(new Widget("Y", false)).Should().BeTrue();
    }

    // --- EmptySpecification ---

    [Fact]
    public void EmptySpecification_Criteria_IsNotNull()
    {
        var spec = new EmptySpecification<Widget>();
        spec.Criteria.Should().NotBeNull();
    }

    [Fact]
    public void EmptySpecification_IsSatisfiedBy_ReturnsFalse_ForAnyEntity()
    {
        var spec = new EmptySpecification<Widget>();
        spec.IsSatisfiedBy(new Widget("X", true)).Should().BeFalse();
        spec.IsSatisfiedBy(new Widget("Y", false)).Should().BeFalse();
    }

    [Fact]
    public void EmptySpecification_MatchesNoEntities_WhenApplied()
    {
        var spec = new EmptySpecification<Widget>();
        var result = Apply(spec, Widgets);
        result.Should().BeEmpty();
    }

    // --- AND identity element: And(All, spec) behaves like spec ---

    [Fact]
    public void And_AllAndActive_UseActiveCriteria()
    {
        var all = new AllSpecification<Widget>();
        var active = new ActiveWidgetSpec();

        var combined = all.And(active);

        // AllSpecification has null criteria → And uses only active's criteria
        combined.Criteria.Should().NotBeNull("And of null-criteria and non-null should use non-null");
        var result = Apply(combined, Widgets);
        result.Should().ContainSingle(w => w.Name == "Alpha");
    }

    // --- OR identity element: Or(Empty, spec) behaves like spec ---

    [Fact]
    public void Or_EmptyOrActive_UseActiveCriteria()
    {
        var empty = new EmptySpecification<Widget>();
        var active = new ActiveWidgetSpec();

        var combined = empty.Or(active);

        // Both operands have non-null criteria → OR combines them
        // Or(always-false, active) = active behavior
        var result = Apply(combined, Widgets);
        result.Should().ContainSingle(w => w.Name == "Alpha");
    }

    // --- OR null-criteria short circuit: Or(All, spec) = null criteria ---

    [Fact]
    public void Or_AllOrActive_HasNullCriteria()
    {
        var all = new AllSpecification<Widget>();
        var active = new ActiveWidgetSpec();

        var combined = all.Or(active);

        // AllSpecification has null criteria → OR with null = null (matches everything)
        combined.Criteria.Should().BeNull(
            "Or with a null-criteria operand must produce null combined criteria");
    }

    [Fact]
    public void Or_AllOrActive_MatchesAllEntities()
    {
        var all = new AllSpecification<Widget>();
        var active = new ActiveWidgetSpec();

        var combined = all.Or(active);
        var result = Apply(combined, Widgets);

        result.Should().HaveCount(2, "null criteria matches all entities");
    }
}
