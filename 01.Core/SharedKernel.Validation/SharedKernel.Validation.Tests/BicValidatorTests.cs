using SharedKernel.Validation.Errors;
using SharedKernel.Validation.Validators;
using Xunit;

namespace SharedKernel.Validation.Tests;

public sealed class BicValidatorTests
{
    // DEUTDEFF / DEUTDEFF500 — Deutsche Bank AG, Frankfurt am Main; the canonical ISO 9362
    // example widely published (Wikipedia's "ISO 9362" article).
    [Theory]
    [InlineData("DEUTDEFF")]
    [InlineData("DEUTDEFF500")]
    [InlineData("deutdeff")] // case-insensitive
    public void Validate_KnownGoodBic_Succeeds(string bic)
    {
        Assert.True(BicValidator.IsValid(bic));
        Assert.True(BicValidator.Validate(bic).IsSuccess);
    }

    [Theory]
    [InlineData("DEUTDEF")]      // 7 chars — too short
    [InlineData("DEUTDEFF1")]    // 9 chars — neither 8 nor 11
    [InlineData("DEU1DEFF")]     // digit in the 4-letter bank code
    [InlineData("")]
    [InlineData(null)]
    public void Validate_MalformedInput_ReturnsInvalidFormat(string? bic)
    {
        var result = BicValidator.Validate(bic);

        Assert.True(result.IsFailure);
        Assert.Equal(ValidationErrorCodes.Bic.InvalidFormat, result.Error.Code);
    }

    [Fact]
    public void Validate_UnrecognizedCountryCode_ReturnsInvalidFormat()
    {
        // "ZZ" is well-formed but not a real ISO 3166-1 country code.
        var result = BicValidator.Validate("DEUTZZFF");

        Assert.True(result.IsFailure);
        Assert.Equal(ValidationErrorCodes.Bic.InvalidFormat, result.Error.Code);
    }
}
