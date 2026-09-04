using SharedKernel.Validation.Errors;
using SharedKernel.Validation.Validators;
using Xunit;

namespace SharedKernel.Validation.Tests;

public sealed class PanValidatorTests
{
    // Well-known published test PANs (Stripe's official test-card catalogue), independently
    // re-verified against the Luhn (mod-10) algorithm by hand before being used here.
    public static TheoryData<string, CardNetwork> KnownGoodPansByNetwork => new()
    {
        { "4242424242424242", CardNetwork.Visa },
        { "5555555555554444", CardNetwork.Mastercard },
        { "378282246310005", CardNetwork.Amex },
        { "6011111111111117", CardNetwork.Discover },
    };

    [Theory]
    [MemberData(nameof(KnownGoodPansByNetwork))]
    public void Validate_KnownGoodPan_PassesLuhnCheck(string pan, CardNetwork _)
    {
        Assert.True(PanValidator.IsValid(pan));
        Assert.True(PanValidator.Validate(pan).IsSuccess);
    }

    [Theory]
    [MemberData(nameof(KnownGoodPansByNetwork))]
    public void DetectNetwork_KnownGoodPan_ReturnsExpectedNetwork(string pan, CardNetwork expected)
    {
        Assert.Equal(expected, PanValidator.DetectNetwork(pan));
    }

    [Fact]
    public void Validate_AcceptsPanWithSpacesAndHyphens()
    {
        Assert.True(PanValidator.IsValid("4242 4242 4242 4242"));
        Assert.True(PanValidator.IsValid("4242-4242-4242-4242"));
    }

    [Theory]
    [InlineData("4242424242424241")] // valid Visa PAN with the last digit mutated
    [InlineData("5555555555554443")] // valid Mastercard PAN with the last digit mutated
    public void Validate_FailsLuhnCheck_ReturnsFailedLuhnCheck(string pan)
    {
        var result = PanValidator.Validate(pan);

        Assert.True(result.IsFailure);
        Assert.Equal(ValidationErrorCodes.Pan.FailedLuhnCheck, result.Error.Code);
    }

    [Theory]
    [InlineData("123456789012")]  // 12 digits, well-formed but not Luhn-valid
    [InlineData("")]
    [InlineData(null)]
    [InlineData("1234")]           // too short
    [InlineData("12345678901234567890")] // too long
    [InlineData("4242-4242-424X-4242")]  // non-digit character
    public void Validate_MalformedOrNonLuhnInput_Fails(string? pan)
    {
        Assert.False(PanValidator.IsValid(pan));
    }

    [Fact]
    public void DetectNetwork_UnrecognizedPrefix_ReturnsUnknown()
    {
        Assert.Equal(CardNetwork.Unknown, PanValidator.DetectNetwork("1234567890123456"));
    }

    [Fact]
    public void DetectNetwork_NullOrEmpty_ReturnsUnknown_NeverThrows()
    {
        Assert.Equal(CardNetwork.Unknown, PanValidator.DetectNetwork(null));
        Assert.Equal(CardNetwork.Unknown, PanValidator.DetectNetwork(string.Empty));
    }
}
