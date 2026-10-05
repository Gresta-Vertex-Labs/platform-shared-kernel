using SharedKernel.Domain.Monetary;
using ProtoMoney = Google.Type.Money;

namespace SharedKernel.Communication.Grpc.Tests.Protobuf;

public sealed class MoneyProtoExtensionsTests
{
    [Theory]
    [InlineData(10, 0, "10")]
    [InlineData(10, 500_000_000, "10.5")]
    [InlineData(0, 10_000_000, "0.01")]
    [InlineData(-5, -250_000_000, "-5.25")]
    [InlineData(0, -1, "-0.000000001")]
    public void A_message_reads_as_its_exact_amount(long units, int nanos, string expected)
    {
        new ProtoMoney { Units = units, Nanos = nanos, CurrencyCode = "EUR" }.ToDecimal()
            .Should().Be(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("10.5", 10, 500_000_000)]
    [InlineData("-1.5", -1, -500_000_000)]
    [InlineData("0.9999999999", 1, 0)]
    [InlineData("-0.9999999999", -1, 0)]
    [InlineData("0.0000000005", 0, 0)]
    [InlineData("0.0000000015", 0, 2)]
    public void An_amount_is_rounded_to_nine_places_before_it_is_split(string amount, long units, int nanos)
    {
        ProtoMoney money = decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture).ToMoneyProto("eur");

        money.Units.Should().Be(units);
        money.Nanos.Should().Be(nanos);
        money.CurrencyCode.Should().Be("EUR");
    }

    [Theory]
    [InlineData(1, -1)]
    [InlineData(-1, 1)]
    [InlineData(0, 1_000_000_000)]
    public void A_message_that_breaks_the_rules_is_refused(long units, int nanos)
    {
        var money = new ProtoMoney { Units = units, Nanos = nanos, CurrencyCode = "EUR" };

        Action read = () => money.ToDecimal();

        read.Should().Throw<ArgumentException>();
        money.ToMoney().Errors.Should().ContainSingle().Which.Code.Should().Be("money.proto.invalid");
    }

    [Fact]
    public void A_whole_part_beyond_64_bits_is_refused()
    {
        Action write = () => decimal.MaxValue.ToMoneyProto("EUR");

        write.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Domain_money_round_trips()
    {
        Money price = Money.Create(12.34m, Currency.Eur).Value;

        Money back = price.ToMoneyProto().ToMoney().Value;

        back.Should().Be(price);
    }

    [Fact]
    public void An_unknown_currency_is_a_validation_error()
    {
        var money = new ProtoMoney { Units = 1, CurrencyCode = "ZZZ" };

        money.ToMoney().Errors.Should().ContainSingle().Which.Code.Should().Be("currency.code.unknown");
    }
}
