using SharedKernel.Primitives.Errors;
using Xunit;

namespace SharedKernel.Validation.Tests;

public sealed class CardNumberTests
{
    // Payment-provider test card numbers (Stripe, Adyen, PayPal documentation).
    [Theory]
    [InlineData("4111111111111111", CardNetwork.Visa)]
    [InlineData("4012888888881881", CardNetwork.Visa)]
    [InlineData("4222222222222", CardNetwork.Visa)]
    [InlineData("5555555555554444", CardNetwork.Mastercard)]
    [InlineData("5105105105105100", CardNetwork.Mastercard)]
    [InlineData("2223003122003222", CardNetwork.Mastercard)]
    [InlineData("378282246310005", CardNetwork.AmericanExpress)]
    [InlineData("371449635398431", CardNetwork.AmericanExpress)]
    [InlineData("6011111111111117", CardNetwork.Discover)]
    [InlineData("6011000990139424", CardNetwork.Discover)]
    [InlineData("3530111333300000", CardNetwork.Jcb)]
    [InlineData("3566002020360505", CardNetwork.Jcb)]
    [InlineData("30569309025904", CardNetwork.DinersClub)]
    [InlineData("38520000023237", CardNetwork.DinersClub)]
    [InlineData("6200000000000005", CardNetwork.UnionPay)]
    [InlineData("6759649826438453", CardNetwork.Maestro)]
    public void Create_ProviderTestCard_DetectsTheNetwork(string number, CardNetwork network)
    {
        CardNumber card = CardNumber.Parse(number, null);

        Assert.Equal(number, card.Value);
        Assert.Equal(network, card.Network);
    }

    [Theory]
    [InlineData("2200", 16, CardNetwork.Mir)]
    [InlineData("2204", 19, CardNetwork.Mir)]
    [InlineData("9792", 16, CardNetwork.Troy)]
    [InlineData("65", 16, CardNetwork.Discover)]
    [InlineData("622126", 16, CardNetwork.UnionPay)]
    [InlineData("5018", 12, CardNetwork.Maestro)]
    [InlineData("4", 15, CardNetwork.Unknown)]
    [InlineData("9792", 19, CardNetwork.Unknown)]
    [InlineData("1", 16, CardNetwork.Unknown)]
    public void Create_RangeAndLength_DecideTheNetwork(string prefix, int length, CardNetwork network)
    {
        Assert.Equal(network, CardNumber.Parse(TestCards.WithLuhn(prefix, length), null).Network);
    }

    [Fact]
    public void ToString_And_DebuggerView_AreMasked()
    {
        CardNumber card = CardNumber.Parse("4111 1111-1111 1111", null);

        Assert.Equal("411111******1111", card.ToString());
        Assert.Equal("411111******1111", card.Masked);
        Assert.Equal($"{card}", card.Masked);
        Assert.Equal("411111", card.Iin);
        Assert.Equal("1111", card.Last4);
        Assert.Equal("4111111111111111", card.Value);
    }

    [Theory]
    [InlineData("41111111111", ValidationErrorCodes.CardNumber.InvalidFormat)]
    [InlineData("41111111111111111111", ValidationErrorCodes.CardNumber.InvalidFormat)]
    [InlineData("4111x11111111111", ValidationErrorCodes.CardNumber.InvalidFormat)]
    [InlineData("4111111111111112", ValidationErrorCodes.CardNumber.InvalidCheckDigit)]
    public void Create_Invalid_ReturnsTheSpecificCode(string number, string code)
    {
        Assert.Equal(code, CardNumber.Create(number).Error.Code);
    }

    [Fact]
    public void Create_RequireKnownNetwork_RejectsAnUnknownNetwork()
    {
        string unknown = TestCards.WithLuhn("1", 16);

        Assert.True(CardNumber.Create(unknown).IsSuccess);
        Assert.Equal(ValidationErrorCodes.CardNumber.UnknownNetwork, CardNumber.Create(unknown, requireKnownNetwork: true).Error.Code);
        Assert.True(CardNumber.Create("4111111111111111", requireKnownNetwork: true).IsSuccess);
    }

    [Fact]
    public void Errors_NeverContainTheNumber()
    {
        foreach (string number in new[] { "4111111111111112", "41111111111", "4111x11111111111" })
        {
            Error error = CardNumber.Create(number).Error;

            Assert.DoesNotContain("4111", error.Message, StringComparison.Ordinal);
            Assert.Empty(error.MessageArguments);
        }
    }
}

internal static class TestCards
{
    // Pads the prefix with zeros to one digit short of length, then appends a Luhn check digit
    // computed independently of the package.
    public static string WithLuhn(string prefix, int length)
    {
        string body = prefix.PadRight(length - 1, '0');
        for (int check = 0; check <= 9; check++)
        {
            string candidate = body + check;
            if (IsLuhnValid(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("unreachable");
    }

    private static bool IsLuhnValid(string digits)
    {
        int sum = 0;
        for (int i = 0; i < digits.Length; i++)
        {
            int d = digits[digits.Length - 1 - i] - '0';
            sum += i % 2 == 1 ? (d * 2 > 9 ? d * 2 - 9 : d * 2) : d;
        }

        return sum % 10 == 0;
    }
}
