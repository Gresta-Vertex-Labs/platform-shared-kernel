using FluentAssertions;
using SharedKernel.AI.Abstractions.Models;

namespace SharedKernel.AI.Abstractions.Tests.Models;

/// <summary>
/// <see cref="VectorValue"/> union tests — each <c>From(...)</c> factory sets the expected
/// <see cref="VectorValueKind"/>; reading the wrong kind-checked accessor throws
/// <see cref="InvalidOperationException"/>; <see cref="VectorValue.From(Guid)"/> normalises to the
/// canonical lowercase hyphenated <c>"D"</c> form; the implicit operators produce the same values as
/// the explicit factories; and <c>ToString()</c> never throws regardless of kind.
/// </summary>
public sealed class VectorValueTests
{
    [Fact]
    public void From_String_SetsKindToString()
    {
        var value = VectorValue.From("hello");

        value.Kind.Should().Be(VectorValueKind.String);
        value.AsString.Should().Be("hello");
    }

    [Fact]
    public void From_Int64_SetsKindToInt64()
    {
        var value = VectorValue.From(42L);

        value.Kind.Should().Be(VectorValueKind.Int64);
        value.AsInt64.Should().Be(42L);
    }

    [Fact]
    public void From_Double_SetsKindToDouble()
    {
        var value = VectorValue.From(3.14);

        value.Kind.Should().Be(VectorValueKind.Double);
        value.AsDouble.Should().Be(3.14);
    }

    [Fact]
    public void From_Boolean_SetsKindToBoolean()
    {
        var value = VectorValue.From(true);

        value.Kind.Should().Be(VectorValueKind.Boolean);
        value.AsBoolean.Should().BeTrue();
    }

    [Fact]
    public void From_DateTimeOffset_SetsKindToDateTimeOffset()
    {
        var now = DateTimeOffset.UtcNow;

        var value = VectorValue.From(now);

        value.Kind.Should().Be(VectorValueKind.DateTimeOffset);
        value.AsDateTimeOffset.Should().Be(now);
    }

    [Fact]
    public void From_Guid_NormalisesToCanonicalLowercaseHyphenated_D_Form()
    {
        var guid = Guid.Parse("6F9619FF-8B86-D011-B42D-00C04FC964FF");

        var value = VectorValue.From(guid);

        value.Kind.Should().Be(VectorValueKind.String);
        value.AsString.Should().Be("6f9619ff-8b86-d011-b42d-00c04fc964ff");
        value.AsString.Should().Be(guid.ToString("D").ToLowerInvariant());
        value.AsString.Should().NotContain("{").And.NotContain("}");
    }

    [Fact]
    public void AsString_WhenKindIsNotString_ThrowsInvalidOperationException()
    {
        var value = VectorValue.From(1L);

        var act = () => value.AsString;

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AsInt64_WhenKindIsNotInt64_ThrowsInvalidOperationException()
    {
        var value = VectorValue.From("text");

        var act = () => value.AsInt64;

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AsDouble_WhenKindIsNotDouble_ThrowsInvalidOperationException()
    {
        var value = VectorValue.From("text");

        var act = () => value.AsDouble;

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AsBoolean_WhenKindIsNotBoolean_ThrowsInvalidOperationException()
    {
        var value = VectorValue.From("text");

        var act = () => value.AsBoolean;

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AsDateTimeOffset_WhenKindIsNotDateTimeOffset_ThrowsInvalidOperationException()
    {
        var value = VectorValue.From("text");

        var act = () => value.AsDateTimeOffset;

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ImplicitOperator_FromString_ProducesSameValueAsExplicitFactory()
    {
        VectorValue implicitValue = "hello";
        var explicitValue = VectorValue.From("hello");

        implicitValue.Should().Be(explicitValue);
    }

    [Fact]
    public void ImplicitOperator_FromInt_ProducesSameValueAsExplicitInt64Factory()
    {
        VectorValue implicitValue = 42;
        var explicitValue = VectorValue.From(42L);

        implicitValue.Should().Be(explicitValue);
    }

    [Fact]
    public void ImplicitOperator_FromLong_ProducesSameValueAsExplicitFactory()
    {
        VectorValue implicitValue = 42L;
        var explicitValue = VectorValue.From(42L);

        implicitValue.Should().Be(explicitValue);
    }

    [Fact]
    public void ImplicitOperator_FromDouble_ProducesSameValueAsExplicitFactory()
    {
        VectorValue implicitValue = 3.14;
        var explicitValue = VectorValue.From(3.14);

        implicitValue.Should().Be(explicitValue);
    }

    [Fact]
    public void ImplicitOperator_FromBoolean_ProducesSameValueAsExplicitFactory()
    {
        VectorValue implicitValue = true;
        var explicitValue = VectorValue.From(true);

        implicitValue.Should().Be(explicitValue);
    }

    [Fact]
    public void ImplicitOperator_FromDateTimeOffset_ProducesSameValueAsExplicitFactory()
    {
        var now = DateTimeOffset.UtcNow;
        VectorValue implicitValue = now;
        var explicitValue = VectorValue.From(now);

        implicitValue.Should().Be(explicitValue);
    }

    [Fact]
    public void ImplicitOperator_FromGuid_ProducesSameValueAsExplicitFactory()
    {
        var guid = Guid.NewGuid();
        VectorValue implicitValue = guid;
        var explicitValue = VectorValue.From(guid);

        implicitValue.Should().Be(explicitValue);
    }

    [Fact]
    public void From_NullString_ThrowsArgumentNullException()
    {
        var act = () => VectorValue.From((string)null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(VectorValueKind.Int64)]
    [InlineData(VectorValueKind.Double)]
    [InlineData(VectorValueKind.Boolean)]
    [InlineData(VectorValueKind.DateTimeOffset)]
    public void ToString_NeverThrows_RegardlessOfKind(VectorValueKind kind)
    {
        VectorValue value = kind switch
        {
            VectorValueKind.Int64 => VectorValue.From(1L),
            VectorValueKind.Double => VectorValue.From(1.0),
            VectorValueKind.Boolean => VectorValue.From(true),
            VectorValueKind.DateTimeOffset => VectorValue.From(DateTimeOffset.UtcNow),
            _ => VectorValue.From("x"),
        };

        var act = () => value.ToString();

        act.Should().NotThrow(
            "formatting must never throw — a record struct's compiler-synthesized ToString would " +
            "call every kind-checked accessor unconditionally");
    }

    [Fact]
    public void ToString_ForStringKind_NeverThrows()
    {
        var value = VectorValue.From("hello");

        var act = () => value.ToString();

        act.Should().NotThrow();
    }
}
