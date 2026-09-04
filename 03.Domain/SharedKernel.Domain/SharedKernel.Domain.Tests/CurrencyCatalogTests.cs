using FluentAssertions;
using SharedKernel.Domain.ValueObjects.Money;

namespace SharedKernel.Domain.Tests;

/// <summary>
/// T-41: P-439b/WO-066 — <see cref="CurrencyCatalog"/> tests.
/// </summary>
public class CurrencyCatalogTests
{
    [Theory]
    [InlineData("USD", 2)]
    [InlineData("EUR", 2)]
    [InlineData("GBP", 2)]
    public void TryGetMinorUnitDigits_DefaultPrecisionCode_ReturnsTrueAndTwo(string code, int expected)
    {
        var result = CurrencyCatalog.TryGetMinorUnitDigits(code, out var digits);

        result.Should().BeTrue();
        digits.Should().Be(expected);
    }

    [Theory]
    [InlineData("JPY")]
    [InlineData("KRW")]
    [InlineData("VND")]
    [InlineData("ISK")]
    [InlineData("CLP")]
    [InlineData("PYG")]
    [InlineData("UGX")]
    [InlineData("RWF")]
    [InlineData("XOF")]
    [InlineData("XAF")]
    [InlineData("XPF")]
    [InlineData("KMF")]
    [InlineData("GNF")]
    [InlineData("DJF")]
    [InlineData("VUV")]
    public void TryGetMinorUnitDigits_ZeroDecimalCode_ReturnsTrueAndZero(string code)
    {
        var result = CurrencyCatalog.TryGetMinorUnitDigits(code, out var digits);

        result.Should().BeTrue();
        digits.Should().Be(0);
    }

    [Theory]
    [InlineData("BHD")]
    [InlineData("KWD")]
    [InlineData("OMR")]
    [InlineData("JOD")]
    [InlineData("TND")]
    [InlineData("LYD")]
    [InlineData("IQD")]
    public void TryGetMinorUnitDigits_ThreeDecimalCode_ReturnsTrueAndThree(string code)
    {
        var result = CurrencyCatalog.TryGetMinorUnitDigits(code, out var digits);

        result.Should().BeTrue();
        digits.Should().Be(3);
    }

    [Fact]
    public void TryGetMinorUnitDigits_UnknownCode_ReturnsFalse()
    {
        var result = CurrencyCatalog.TryGetMinorUnitDigits("ZZZ", out var digits);

        result.Should().BeFalse();
        digits.Should().Be(0);
    }

    [Fact]
    public void TryGetMinorUnitDigits_NullCode_ReturnsFalse()
    {
        var result = CurrencyCatalog.TryGetMinorUnitDigits(null!, out var digits);

        result.Should().BeFalse();
        digits.Should().Be(0);
    }

    [Theory]
    [InlineData("USD", true)]
    [InlineData("JPY", true)]
    [InlineData("BHD", true)]
    [InlineData("ZZZ", false)]
    [InlineData("usd", false)] // catalog is case-sensitive; normalization is Currency's job
    public void IsKnownCode_MirrorsTryGetMinorUnitDigits(string code, bool expected)
    {
        CurrencyCatalog.IsKnownCode(code).Should().Be(expected);
    }

    [Fact]
    public void IsKnownCode_NullCode_ReturnsFalse()
    {
        CurrencyCatalog.IsKnownCode(null!).Should().BeFalse();
    }
}
