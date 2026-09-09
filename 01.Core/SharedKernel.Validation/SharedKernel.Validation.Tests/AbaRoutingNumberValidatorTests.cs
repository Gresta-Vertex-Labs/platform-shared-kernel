using SharedKernel.Validation.Errors;
using SharedKernel.Validation.Validators;
using Xunit;

namespace SharedKernel.Validation.Tests;

public sealed class AbaRoutingNumberValidatorTests
{
    // Real, well-known US ABA routing numbers. "111000025" (Bank of America, Virginia) is the
    // worked example published on Wikipedia's "ABA routing transit number" article, which shows
    // the (3,7,1)-weighted-sum-mod-10 calculation explicitly. The other two are widely-published,
    // real bank routing numbers (JPMorgan Chase NY; Wells Fargo CA) — each independently
    // recomputed against the same published checksum formula and confirmed to satisfy it before
    // being used here, not merely trusted from a secondary source.
    [Theory]
    [InlineData("111000025")] // Bank of America, Virginia (Wikipedia worked example)
    [InlineData("021000021")] // JPMorgan Chase, New York
    [InlineData("121000248")] // Wells Fargo, California
    public void Validate_KnownGoodRoutingNumber_Succeeds(string routingNumber)
    {
        Assert.True(AbaRoutingNumberValidator.IsValid(routingNumber));

        var result = AbaRoutingNumberValidator.Validate(routingNumber);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_SurroundingWhitespace_IsTrimmedAndSucceeds()
    {
        var result = AbaRoutingNumberValidator.Validate("  111000025  ");

        Assert.True(result.IsSuccess);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Validate_NullOrEmpty_ReturnsInvalidFormat(string? routingNumber)
    {
        var result = AbaRoutingNumberValidator.Validate(routingNumber);

        Assert.True(result.IsFailure);
        Assert.Equal(ValidationErrorCodes.AbaRoutingNumber.InvalidFormat, result.Error.Code);
    }

    [Theory]
    [InlineData("12345678")]   // 8 digits — one short
    [InlineData("1234567890")] // 10 digits — one over
    public void Validate_WrongLength_ReturnsInvalidFormat(string routingNumber)
    {
        var result = AbaRoutingNumberValidator.Validate(routingNumber);

        Assert.True(result.IsFailure);
        Assert.Equal(ValidationErrorCodes.AbaRoutingNumber.InvalidFormat, result.Error.Code);
    }

    [Fact]
    public void Validate_ContainsNonDigitCharacter_ReturnsInvalidFormat()
    {
        var result = AbaRoutingNumberValidator.Validate("11100002A");

        Assert.True(result.IsFailure);
        Assert.Equal(ValidationErrorCodes.AbaRoutingNumber.InvalidFormat, result.Error.Code);
    }

    [Fact]
    public void Validate_CorruptedChecksum_ReturnsFailedChecksum()
    {
        // "111000025" with the last digit mutated (5 -> 4); independently recomputed against the
        // published (3,7,1) checksum formula and confirmed the weighted sum is no longer congruent
        // to 0 modulo 10.
        var result = AbaRoutingNumberValidator.Validate("111000024");

        Assert.True(result.IsFailure);
        Assert.Equal(ValidationErrorCodes.AbaRoutingNumber.FailedChecksum, result.Error.Code);
    }
}
