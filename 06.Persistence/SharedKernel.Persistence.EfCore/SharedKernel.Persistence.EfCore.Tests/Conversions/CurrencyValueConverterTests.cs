using FluentAssertions;
using SharedKernel.Domain.ValueObjects.Money;
using SharedKernel.Persistence.EfCore.Conversions;

namespace SharedKernel.Persistence.EfCore.Tests.Conversions;

/// <summary>
/// WO-066/P-440/D-104/C-146 — <see cref="CurrencyValueConverter"/> unit tests.
/// </summary>
public sealed class CurrencyValueConverterTests
{
    [Fact]
    public void Converter_ToProvider_ReturnsIsoCode()
    {
        // Arrange
        var converter = new CurrencyValueConverter();

        // Act
        var toProvider = converter.ConvertToProvider;
        var result = toProvider!(Currency.Usd);

        // Assert
        result.Should().Be("USD");
    }

    [Fact]
    public void Converter_FromProvider_ReturnsCurrency()
    {
        // Arrange
        var converter = new CurrencyValueConverter();

        // Act
        var fromProvider = converter.ConvertFromProvider;
        var result = fromProvider!("EUR");

        // Assert
        result.Should().BeOfType<Currency>();
        ((Currency)result!).Code.Should().Be("EUR");
    }

    [Fact]
    public void Converter_FromProvider_UnknownCode_Throws()
    {
        // Arrange
        var converter = new CurrencyValueConverter();
        var fromProvider = converter.ConvertFromProvider;

        // Act
        var act = () => fromProvider!("ZZZ");

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ZZZ*");
    }

    [Fact]
    public void Converter_FromProvider_MalformedCode_Throws()
    {
        // Arrange
        var converter = new CurrencyValueConverter();
        var fromProvider = converter.ConvertFromProvider;

        // Act
        var act = () => fromProvider!("bad-code");

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void RoundTrip_PreservesCurrencyIdentity()
    {
        // Arrange
        var converter = new CurrencyValueConverter();

        // Act
        var provider = converter.ConvertToProvider!(Currency.Jpy);
        var roundTripped = (Currency)converter.ConvertFromProvider!(provider)!;

        // Assert
        roundTripped.Should().Be(Currency.Jpy);
        roundTripped.Code.Should().Be("JPY");
        roundTripped.MinorUnitDigits.Should().Be(0);
    }
}
