using System.Reflection;
using FluentAssertions;
using SharedKernel.Search.Abstractions.Models;

namespace SharedKernel.Search.Abstractions.Tests.Models;

/// <summary>
/// T-02: <see cref="SearchFilter"/> closed-hierarchy tests — every static factory produces the
/// expected node type and property values; <see cref="SearchFilter.Between"/> rejects
/// <see cref="SearchValueKind.String"/> and <see cref="SearchValueKind.Boolean"/> bounds; and a
/// reflection assertion locks every one of the eight node constructors as non-public so no assembly
/// outside <c>SharedKernel.Search.Abstractions</c> can construct a node.
/// </summary>
public sealed class SearchFilterTests
{
    [Fact]
    public void Eq_ProducesEqualFilter_WithFieldAndValue()
    {
        var filter = SearchFilter.Eq("status", SearchValue.From("active"));

        var node = filter.Should().BeOfType<EqualFilter>().Subject;
        node.Field.Should().Be("status");
        node.Value.AsString.Should().Be("active");
    }

    [Fact]
    public void Ne_ProducesNotEqualFilter_WithFieldAndValue()
    {
        var filter = SearchFilter.Ne("status", SearchValue.From("archived"));

        var node = filter.Should().BeOfType<NotEqualFilter>().Subject;
        node.Field.Should().Be("status");
        node.Value.AsString.Should().Be("archived");
    }

    [Fact]
    public void In_ProducesInFilter_WithFieldAndValues()
    {
        var filter = SearchFilter.In("category", SearchValue.From("a"), SearchValue.From("b"));

        var node = filter.Should().BeOfType<InFilter>().Subject;
        node.Field.Should().Be("category");
        node.Values.Should().HaveCount(2);
        node.Values[0].AsString.Should().Be("a");
        node.Values[1].AsString.Should().Be("b");
    }

    [Fact]
    public void In_WithSingleValue_ProducesInFilter_WithOneValue()
    {
        var filter = SearchFilter.In("category", SearchValue.From("only"));

        var node = filter.Should().BeOfType<InFilter>().Subject;
        node.Values.Should().ContainSingle().Which.AsString.Should().Be("only");
    }

    [Fact]
    public void Between_WithInt64Bounds_ProducesRangeFilter()
    {
        var filter = SearchFilter.Between("price", SearchValue.From(10L), SearchValue.From(100L));

        var node = filter.Should().BeOfType<RangeFilter>().Subject;
        node.Field.Should().Be("price");
        node.From!.Value.AsInt64.Should().Be(10L);
        node.To!.Value.AsInt64.Should().Be(100L);
        node.FromInclusive.Should().BeTrue();
        node.ToInclusive.Should().BeTrue();
    }

    [Fact]
    public void Between_WithDoubleBounds_ProducesRangeFilter()
    {
        var filter = SearchFilter.Between("rating", SearchValue.From(1.5), SearchValue.From(4.5));

        var node = filter.Should().BeOfType<RangeFilter>().Subject;
        node.From!.Value.AsDouble.Should().Be(1.5);
        node.To!.Value.AsDouble.Should().Be(4.5);
    }

    [Fact]
    public void Between_WithDateTimeOffsetBounds_ProducesRangeFilter()
    {
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero);

        var filter = SearchFilter.Between("createdAt", SearchValue.From(from), SearchValue.From(to));

        var node = filter.Should().BeOfType<RangeFilter>().Subject;
        node.From!.Value.AsDateTimeOffset.Should().Be(from);
        node.To!.Value.AsDateTimeOffset.Should().Be(to);
    }

    [Fact]
    public void Between_WithExclusiveBounds_SetsInclusiveFlagsFalse()
    {
        var filter = SearchFilter.Between(
            "price", SearchValue.From(10L), SearchValue.From(100L), fromInclusive: false, toInclusive: false);

        var node = filter.Should().BeOfType<RangeFilter>().Subject;
        node.FromInclusive.Should().BeFalse();
        node.ToInclusive.Should().BeFalse();
    }

    [Fact]
    public void Between_WithOpenLowerBound_AllowsNullFrom()
    {
        var filter = SearchFilter.Between("price", from: null, to: SearchValue.From(100L));

        var node = filter.Should().BeOfType<RangeFilter>().Subject;
        node.From.Should().BeNull();
        node.To.Should().NotBeNull();
    }

    [Fact]
    public void Between_WithStringBound_ThrowsArgumentException()
    {
        var act = () => SearchFilter.Between("name", SearchValue.From("a"), SearchValue.From("z"));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Between_WithBooleanBound_ThrowsArgumentException()
    {
        var act = () => SearchFilter.Between("isActive", SearchValue.From(false), SearchValue.From(true));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Between_WithStringUpperBoundOnly_ThrowsArgumentException()
    {
        var act = () => SearchFilter.Between("name", from: null, to: SearchValue.From("z"));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Exists_ProducesExistsFilter_WithField()
    {
        var filter = SearchFilter.Exists("optionalField");

        var node = filter.Should().BeOfType<ExistsFilter>().Subject;
        node.Field.Should().Be("optionalField");
    }

    [Fact]
    public void All_ProducesAndFilter_WithOperandsInOrder()
    {
        var a = SearchFilter.Eq("a", SearchValue.From(1L));
        var b = SearchFilter.Eq("b", SearchValue.From(2L));

        var filter = SearchFilter.All(a, b);

        var node = filter.Should().BeOfType<AndFilter>().Subject;
        node.Operands.Should().Equal(a, b);
    }

    [Fact]
    public void Any_ProducesOrFilter_WithOperandsInOrder()
    {
        var a = SearchFilter.Eq("a", SearchValue.From(1L));
        var b = SearchFilter.Eq("b", SearchValue.From(2L));

        var filter = SearchFilter.Any(a, b);

        var node = filter.Should().BeOfType<OrFilter>().Subject;
        node.Operands.Should().Equal(a, b);
    }

    [Fact]
    public void All_WithNoOperands_ProducesAndFilter_WithEmptyOperands()
    {
        var filter = SearchFilter.All();

        var node = filter.Should().BeOfType<AndFilter>().Subject;
        node.Operands.Should().BeEmpty();
    }

    [Fact]
    public void Any_WithNoOperands_ProducesOrFilter_WithEmptyOperands()
    {
        var filter = SearchFilter.Any();

        var node = filter.Should().BeOfType<OrFilter>().Subject;
        node.Operands.Should().BeEmpty();
    }

    [Fact]
    public void Negate_ProducesNotFilter_WrappingOperand()
    {
        var inner = SearchFilter.Eq("a", SearchValue.From(1L));

        var filter = SearchFilter.Negate(inner);

        var node = filter.Should().BeOfType<NotFilter>().Subject;
        node.Operand.Should().Be(inner);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Eq_WithNullOrWhiteSpaceField_ThrowsArgumentException(string? field)
    {
        var act = () => SearchFilter.Eq(field!, SearchValue.From(1L));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AllEightNodeConstructors_AreNonPublic()
    {
        Type[] nodeTypes =
        [
            typeof(EqualFilter), typeof(NotEqualFilter), typeof(InFilter), typeof(RangeFilter),
            typeof(ExistsFilter), typeof(AndFilter), typeof(OrFilter), typeof(NotFilter),
        ];

        nodeTypes.Should().HaveCount(8, "the SearchFilter AST is closed at exactly eight nodes");

        foreach (var nodeType in nodeTypes)
        {
            var constructors = nodeType.GetConstructors(
                BindingFlags.Public | BindingFlags.Instance);

            constructors.Should().BeEmpty(
                $"{nodeType.Name} must not expose a public constructor — the static factories on " +
                "SearchFilter are the only sanctioned construction path");
        }
    }

    [Fact]
    public void SearchFilter_BaseConstructor_IsNotPublic()
    {
        var constructors = typeof(SearchFilter).GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        constructors.Should().BeEmpty("SearchFilter's base constructor is private protected");
    }
}
