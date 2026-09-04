using SharedKernel.Validation.Errors;
using SharedKernel.Validation.Validators;
using Xunit;

namespace SharedKernel.Validation.Tests;

public sealed class IsoCurrencyValidatorTests
{
    [Theory]
    [InlineData("USD")]
    [InlineData("EUR")]
    [InlineData("GBP")]
    [InlineData("JPY")]
    [InlineData("TRY")]
    [InlineData("usd")] // case-insensitive
    public void Validate_KnownGoodCode_Succeeds(string code)
    {
        Assert.True(IsoCurrencyValidator.IsValid(code));
        Assert.True(IsoCurrencyValidator.Validate(code).IsSuccess);
    }

    [Theory]
    [InlineData("XYZ")] // well-formed, not a real ISO 4217 code
    [InlineData("ZZZ")]
    [InlineData("US")]  // wrong length
    [InlineData("USDD")]
    [InlineData("123")]
    [InlineData("")]
    [InlineData(null)]
    public void Validate_UnknownOrMalformedCode_ReturnsUnknownCode(string? code)
    {
        var result = IsoCurrencyValidator.Validate(code);

        Assert.True(result.IsFailure);
        Assert.Equal(ValidationErrorCodes.Currency.UnknownCode, result.Error.Code);
    }
}
