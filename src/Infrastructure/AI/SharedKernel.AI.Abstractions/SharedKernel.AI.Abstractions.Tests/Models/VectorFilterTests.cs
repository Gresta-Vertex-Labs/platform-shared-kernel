using System.Reflection;
using FluentAssertions;
using SharedKernel.AI.Abstractions.Models;

namespace SharedKernel.AI.Abstractions.Tests.Models;

/// <summary>
/// <see cref="VectorFilter"/> closed-hierarchy tests — every static factory produces the expected node
/// type and property values; <see cref="VectorFilter.Between"/> rejects
/// <see cref="VectorValueKind.String"/> and <see cref="VectorValueKind.Boolean"/> bounds; and a
/// reflection assertion locks every one of the eight node constructors as non-public so no assembly
/// outside <c>SharedKernel.AI.Abstractions</c> can construct a node.
/// </summary>
public sealed class VectorFilterTests
{
    [Fact]
    public void Eq_ProducesEqualFilter_WithFieldAndValue()
    {
        var filter = VectorFilter.Eq("status", VectorValue.From("active"));

        var node = filter.Should().BeOfType<EqualFilter>().Subject;
        node.Field.Should().Be("status");
        node.Value.AsString.Should().Be("active");
    }

    [Fact]
    public void Ne_ProducesNotEqualFilter_WithFieldAndValue()
    {
        var filter = VectorFilter.Ne("status", VectorValue.From("archived"));

        var node = filter.Should().BeOfType<NotEqualFilter>().Subject;
        node.Field.Should().Be("status");
        node.Value.AsString.Should().Be("archived");
    }

    [Fact]
    public void In_ProducesInFilter_WithFieldAndValues()
    {
        var filter = VectorFilter.In("category", VectorValue.From("a"), VectorValue.From("b"));

        var node = filter.Should().BeOfType<InFilter>().Subject;
        node.Field.Should().Be("category");
        node.Values.Should().HaveCount(2);
        node.Values[0].AsString.Should().Be("a");
        node.Values[1].AsString.Should().Be("b");
    }

    [Fact]
    public void In_WithSingleValue_ProducesInFilter_WithOneValue()
    {
        var filter = VectorFilter.In("category", VectorValue.From("only"));

        var node = filter.Should().BeOfType<InFilter>().Subject;
        node.Values.Should().ContainSingle().Which.AsString.Should().Be("only");
    }

    [Fact]
    public void Between_WithInt64Bounds_ProducesRangeFilter()
    {
        var filter = VectorFilter.Between("price", VectorValue.From(10L), VectorValue.From(100L));

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
        var filter = VectorFilter.Between("score", VectorValue.From(0.5), VectorValue.From(0.9));

        var node = filter.Should().BeOfType<RangeFilter>().Subject;
        node.From!.Value.AsDouble.Should().Be(0.5);
        node.To!.Value.AsDouble.Should().Be(0.9);
    }

    [Fact]
    public void Between_WithDateTimeOffsetBounds_ProducesRangeFilter()
    {
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero);

        var filter = VectorFilter.Between("createdAt", VectorValue.From(from), VectorValue.From(to));

        var node = filter.Should().BeOfType<RangeFilter>().Subject;
        node.From!.Value.AsDateTimeOffset.Should().Be(from);
        node.To!.Value.AsDateTimeOffset.Should().Be(to);
    }

    [Fact]
    public void Between_WithExclusiveBounds_SetsInclusiveFlagsFalse()
    {
        var filter = VectorFilter.Between(
            "price", VectorValue.From(10L), VectorValue.From(100L), fromInclusive: false, toInclusive: false);

        var node = filter.Should().BeOfType<RangeFilter>().Subject;
        node.FromInclusive.Should().BeFalse();
        node.ToInclusive.Should().BeFalse();
    }

    [Fact]
    public void Between_WithOpenLowerBound_AllowsNullFrom()
    {
        var filter = VectorFilter.Between("price", from: null, to: VectorValue.From(100L));

        var node = filter.Should().BeOfType<RangeFilter>().Subject;
        node.From.Should().BeNull();
        node.To.Should().NotBeNull();
    }

    [Fact]
    public void Between_WithStringBound_ThrowsArgumentException()
    {
        var act = () => VectorFilter.Between("name", VectorValue.From("a"), VectorValue.From("z"));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Between_WithBooleanBound_ThrowsArgumentException()
    {
        var act = () => VectorFilter.Between("isActive", VectorValue.From(false), VectorValue.From(true));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Between_WithStringUpperBoundOnly_ThrowsArgumentException()
    {
        var act = () => VectorFilter.Between("name", from: null, to: VectorValue.From("z"));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Exists_ProducesExistsFilter_WithField()
    {
        var filter = VectorFilter.Exists("optionalField");

        var node = filter.Should().BeOfType<ExistsFilter>().Subject;
        node.Field.Should().Be("optionalField");
    }

    [Fact]
    public void All_ProducesAndFilter_WithOperandsInOrder()
    {
        var a = VectorFilter.Eq("a", VectorValue.From(1L));
        var b = VectorFilter.Eq("b", VectorValue.From(2L));

        var filter = VectorFilter.All(a, b);

        var node = filter.Should().BeOfType<AndFilter>().Subject;
        node.Operands.Should().Equal(a, b);
    }

    [Fact]
    public void Any_ProducesOrFilter_WithOperandsInOrder()
    {
        var a = VectorFilter.Eq("a", VectorValue.From(1L));
        var b = VectorFilter.Eq("b", VectorValue.From(2L));

        var filter = VectorFilter.Any(a, b);

        var node = filter.Should().BeOfType<OrFilter>().Subject;
        node.Operands.Should().Equal(a, b);
    }

    [Fact]
    public void All_WithNoOperands_ProducesAndFilter_WithEmptyOperands()
    {
        var filter = VectorFilter.All();

        var node = filter.Should().BeOfType<AndFilter>().Subject;
        node.Operands.Should().BeEmpty();
    }

    [Fact]
    public void Any_WithNoOperands_ProducesOrFilter_WithEmptyOperands()
    {
        var filter = VectorFilter.Any();

        var node = filter.Should().BeOfType<OrFilter>().Subject;
        node.Operands.Should().BeEmpty();
    }

    [Fact]
    public void Negate_ProducesNotFilter_WrappingOperand()
    {
        var inner = VectorFilter.Eq("a", VectorValue.From(1L));

        var filter = VectorFilter.Negate(inner);

        var node = filter.Should().BeOfType<NotFilter>().Subject;
        node.Operand.Should().Be(inner);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Eq_WithNullOrWhiteSpaceField_ThrowsArgumentException(string? field)
    {
        var act = () => VectorFilter.Eq(field!, VectorValue.From(1L));

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

        nodeTypes.Should().HaveCount(8, "the VectorFilter AST is closed at exactly eight nodes");

        foreach (var nodeType in nodeTypes)
        {
            var constructors = nodeType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);

            constructors.Should().BeEmpty(
                $"{nodeType.Name} must not expose a public constructor — the static factories on " +
                "VectorFilter are the only sanctioned construction path");
        }
    }

    [Fact]
    public void VectorFilter_BaseConstructor_IsNotPublic()
    {
        var constructors = typeof(VectorFilter).GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        constructors.Should().BeEmpty("VectorFilter's base constructor is private protected");
    }
}
