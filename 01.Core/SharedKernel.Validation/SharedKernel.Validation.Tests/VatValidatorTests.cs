using SharedKernel.Validation.Errors;
using SharedKernel.Validation.Validators;
using Xunit;

namespace SharedKernel.Validation.Tests;

public sealed class VatValidatorTests
{
    [Theory]
    [InlineData("DE123456789")]
    [InlineData("GB999999973")] // HMRC's published test VAT number
    [InlineData("FR40303265045")]
    [InlineData("de123456789")] // case-insensitive
    public void Validate_BaselineFormatMatches_Succeeds(string vat)
    {
        Assert.True(VatValidator.IsValid(vat));
        Assert.True(VatValidator.Validate(vat).IsSuccess);
    }

    [Theory]
    [InlineData("123456789")]  // no country-letter prefix
    [InlineData("D3123456789")] // second character is not a letter
    [InlineData("DE1")]         // only 1 alphanumeric character after the prefix
    [InlineData("")]
    [InlineData(null)]
    public void Validate_MalformedInput_ReturnsInvalidFormat(string? vat)
    {
        var result = VatValidator.Validate(vat);

        Assert.True(result.IsFailure);
        Assert.Equal(ValidationErrorCodes.Vat.InvalidFormat, result.Error.Code);
    }
}
