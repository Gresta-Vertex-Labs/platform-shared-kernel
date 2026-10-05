using FluentAssertions;
using SharedKernel.Search.Abstractions.Models;

namespace SharedKernel.Search.Abstractions.Tests.Models;

/// <summary>
/// T-03: <see cref="SearchValue"/> union tests — each <c>From(...)</c> factory sets the expected
/// <see cref="SearchValueKind"/>; reading the wrong kind-checked accessor throws
/// <see cref="InvalidOperationException"/>; <see cref="SearchValue.From(Guid)"/> normalises to the
/// canonical lowercase hyphenated <c>"D"</c> form; and the implicit operators produce the same values
/// as the explicit factories.
/// </summary>
public sealed class SearchValueTests
{
    [Fact]
    public void From_String_SetsKindToString()
    {
        var value = SearchValue.From("hello");

        value.Kind.Should().Be(SearchValueKind.String);
        value.AsString.Should().Be("hello");
    }

    [Fact]
    public void From_Int64_SetsKindToInt64()
    {
        var value = SearchValue.From(42L);

        value.Kind.Should().Be(SearchValueKind.Int64);
        value.AsInt64.Should().Be(42L);
    }

    [Fact]
    public void From_Double_SetsKindToDouble()
    {
        var value = SearchValue.From(3.14);

        value.Kind.Should().Be(SearchValueKind.Double);
        value.AsDouble.Should().Be(3.14);
    }

    [Fact]
    public void From_Boolean_SetsKindToBoolean()
    {
        var value = SearchValue.From(true);

        value.Kind.Should().Be(SearchValueKind.Boolean);
        value.AsBoolean.Should().BeTrue();
    }

    [Fact]
    public void From_DateTimeOffset_SetsKindToDateTimeOffset()
    {
        var now = DateTimeOffset.UtcNow;

        var value = SearchValue.From(now);

        value.Kind.Should().Be(SearchValueKind.DateTimeOffset);
        value.AsDateTimeOffset.Should().Be(now);
    }

    [Fact]
    public void From_Guid_NormalisesToCanonicalLowercaseHyphenated_D_Form()
    {
        var guid = Guid.Parse("6F9619FF-8B86-D011-B42D-00C04FC964FF");

        var value = SearchValue.From(guid);

        value.Kind.Should().Be(SearchValueKind.String);
        value.AsString.Should().Be("6f9619ff-8b86-d011-b42d-00c04fc964ff");
        value.AsString.Should().Be(guid.ToString("D").ToLowerInvariant());
        value.AsString.Should().NotContain("{").And.NotContain("}");
    }

    [Fact]
    public void AsString_WhenKindIsNotString_ThrowsInvalidOperationException()
    {
        var value = SearchValue.From(1L);

        var act = () => value.AsString;

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AsInt64_WhenKindIsNotInt64_ThrowsInvalidOperationException()
    {
        var value = SearchValue.From("text");

        var act = () => value.AsInt64;

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AsDouble_WhenKindIsNotDouble_ThrowsInvalidOperationException()
    {
        var value = SearchValue.From("text");

        var act = () => value.AsDouble;

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AsBoolean_WhenKindIsNotBoolean_ThrowsInvalidOperationException()
    {
        var value = SearchValue.From("text");

        var act = () => value.AsBoolean;

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AsDateTimeOffset_WhenKindIsNotDateTimeOffset_ThrowsInvalidOperationException()
    {
        var value = SearchValue.From("text");

        var act = () => value.AsDateTimeOffset;

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ImplicitOperator_FromString_ProducesSameValueAsExplicitFactory()
    {
        SearchValue implicitValue = "hello";
        var explicitValue = SearchValue.From("hello");

        implicitValue.Should().Be(explicitValue);
    }

    [Fact]
    public void ImplicitOperator_FromInt_ProducesSameValueAsExplicitInt64Factory()
    {
        SearchValue implicitValue = 42;
        var explicitValue = SearchValue.From(42L);

        implicitValue.Should().Be(explicitValue);
    }

    [Fact]
    public void ImplicitOperator_FromLong_ProducesSameValueAsExplicitFactory()
    {
        SearchValue implicitValue = 42L;
        var explicitValue = SearchValue.From(42L);

        implicitValue.Should().Be(explicitValue);
    }

    [Fact]
    public void ImplicitOperator_FromDouble_ProducesSameValueAsExplicitFactory()
    {
        SearchValue implicitValue = 3.14;
        var explicitValue = SearchValue.From(3.14);

        implicitValue.Should().Be(explicitValue);
    }

    [Fact]
    public void ImplicitOperator_FromBoolean_ProducesSameValueAsExplicitFactory()
    {
        SearchValue implicitValue = true;
        var explicitValue = SearchValue.From(true);

        implicitValue.Should().Be(explicitValue);
    }

    [Fact]
    public void ImplicitOperator_FromDateTimeOffset_ProducesSameValueAsExplicitFactory()
    {
        var now = DateTimeOffset.UtcNow;
        SearchValue implicitValue = now;
        var explicitValue = SearchValue.From(now);

        implicitValue.Should().Be(explicitValue);
    }

    [Fact]
    public void ImplicitOperator_FromGuid_ProducesSameValueAsExplicitFactory()
    {
        var guid = Guid.NewGuid();
        SearchValue implicitValue = guid;
        var explicitValue = SearchValue.From(guid);

        implicitValue.Should().Be(explicitValue);
    }

    [Fact]
    public void From_NullString_ThrowsArgumentNullException()
    {
        var act = () => SearchValue.From((string)null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(SearchValueKind.Int64)]
    [InlineData(SearchValueKind.Double)]
    [InlineData(SearchValueKind.Boolean)]
    [InlineData(SearchValueKind.DateTimeOffset)]
    public void ToString_NeverThrows_RegardlessOfKind(SearchValueKind kind)
    {
        SearchValue value = kind switch
        {
            SearchValueKind.Int64 => SearchValue.From(1L),
            SearchValueKind.Double => SearchValue.From(1.0),
            SearchValueKind.Boolean => SearchValue.From(true),
            SearchValueKind.DateTimeOffset => SearchValue.From(DateTimeOffset.UtcNow),
            _ => SearchValue.From("x"),
        };

        var act = () => value.ToString();

        act.Should().NotThrow(
            "formatting must never throw — a record struct's compiler-synthesized ToString would " +
            "call every kind-checked accessor unconditionally");
    }

    [Fact]
    public void ToString_ForStringKind_NeverThrows()
    {
        var value = SearchValue.From("hello");

        var act = () => value.ToString();

        act.Should().NotThrow();
    }
}
