using FluentAssertions;
using SharedKernel.Domain.ValueObjects.Money;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Domain.Tests;

/// <summary>
/// T-42: P-439c/WO-066 — <see cref="Currency"/> tests.
/// </summary>
public class CurrencyTests
{
    [Fact]
    public void Create_LowercaseKnownCode_NormalizesAndSucceeds()
    {
        var result = Currency.Create("usd");

        result.IsSuccess.Should().BeTrue();
        result.Value.Code.Should().Be("USD");
    }

    [Fact]
    public void Create_WithSurroundingWhitespace_TrimsAndSucceeds()
    {
        var result = Currency.Create("  eur  ");

        result.IsSuccess.Should().BeTrue();
        result.Value.Code.Should().Be("EUR");
    }

    [Fact]
    public void Create_WrongLength_Fails()
    {
        var result = Currency.Create("US");

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public void Create_WellFormedButUnknownCode_Fails()
    {
        var result = Currency.Create("ZZZ");

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public void Create_NonLetterCharacters_Fails()
    {
        var result = Currency.Create("U5D");

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public void Create_Null_Fails()
    {
        var result = Currency.Create(null!);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public void Equality_SameCode_AreEqual()
    {
        var left = Currency.Create("USD").Value;
        var right = Currency.Create("usd").Value;

        left.Should().Be(right);
        (left == right).Should().BeTrue();
    }

    [Fact]
    public void Equality_DifferentCode_AreNotEqual()
    {
        var usd = Currency.Create("USD").Value;
        var eur = Currency.Create("EUR").Value;

        usd.Should().NotBe(eur);
    }

    [Fact]
    public void MinorUnitDigits_ReflectsCatalogValue()
    {
        Currency.Create("USD").Value.MinorUnitDigits.Should().Be(2);
        Currency.Create("JPY").Value.MinorUnitDigits.Should().Be(0);
        Currency.Create("BHD").Value.MinorUnitDigits.Should().Be(3);
    }

    [Fact]
    public void WellKnownStatics_ConstructSuccessfully()
    {
        Currency.Usd.Code.Should().Be("USD");
        Currency.Eur.Code.Should().Be("EUR");
        Currency.Gbp.Code.Should().Be("GBP");
        Currency.Jpy.Code.Should().Be("JPY");
    }

    [Fact]
    public void Jpy_MinorUnitDigits_IsZero()
    {
        Currency.Jpy.MinorUnitDigits.Should().Be(0);
    }
}
