using SharedKernel.Communication.Grpc.Protobuf;

namespace SharedKernel.Communication.Grpc.Tests.Protobuf;

public sealed class MoneyProtoExtensionsTests
{
    [Theory]
    [InlineData(10, 0, 10.0)]
    [InlineData(10, 500_000_000, 10.5)]
    [InlineData(10, 990_000_000, 10.99)]
    [InlineData(0, 10_000_000, 0.01)]
    [InlineData(-5, -250_000_000, -5.25)]
    [InlineData(0, 0, 0.0)]
    public void ToDecimal_RoundTrips_WithoutPrecisionLoss(long units, int nanos, decimal expected)
    {
        // Arrange
        var money = new Google.Type.Money { Units = units, Nanos = nanos, CurrencyCode = "USD" };

        // Act
        var result = money.ToDecimal();

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(10.5, "USD")]
    [InlineData(99.99, "EUR")]
    [InlineData(0.01, "GBP")]
    [InlineData(-5.25, "JPY")]
    [InlineData(0.0, "USD")]
    [InlineData(1000000.123456789, "USD")]
    public void ToMoneyProto_RoundTrips_WithoutPrecisionLoss(decimal value, string currency)
    {
        // Arrange + Act
        var money = value.ToMoneyProto(currency);
        var roundTripped = money.ToDecimal();

        // Assert
        roundTripped.Should().Be(value, $"decimal → Money → decimal round-trip must be lossless for {value}");
        money.CurrencyCode.Should().Be(currency);
    }

    [Fact]
    public void ToMoneyProto_SetsCorrectFields()
    {
        // Arrange
        const decimal value = 10.5m;

        // Act
        var money = value.ToMoneyProto("USD");

        // Assert
        money.Units.Should().Be(10L);
        money.Nanos.Should().Be(500_000_000);
        money.CurrencyCode.Should().Be("USD");
    }

    [Fact]
    public void ToMoneyProto_NegativeValue_SetsMatchingSignOnNanos()
    {
        // Arrange
        const decimal value = -5.25m;

        // Act
        var money = value.ToMoneyProto("USD");

        // Assert
        money.Units.Should().Be(-5L);
        money.Nanos.Should().Be(-250_000_000);
    }

    [Fact]
    public void ToDecimal_ZeroMoney_ReturnsZero()
    {
        // Arrange
        var money = new Google.Type.Money { Units = 0, Nanos = 0, CurrencyCode = "USD" };

        // Act
        var result = money.ToDecimal();

        // Assert
        result.Should().Be(0m);
    }

    [Fact]
    public void ToMoneyProto_LargeValue_Converts()
    {
        // Arrange
        const decimal value = 9_999_999_999.99m;

        // Act
        var money = value.ToMoneyProto("USD");
        var roundTripped = money.ToDecimal();

        // Assert
        roundTripped.Should().Be(value);
    }
}
