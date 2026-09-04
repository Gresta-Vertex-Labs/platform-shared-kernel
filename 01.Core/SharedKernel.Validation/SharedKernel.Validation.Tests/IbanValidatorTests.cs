using SharedKernel.Validation.Errors;
using SharedKernel.Validation.Validators;
using Xunit;

namespace SharedKernel.Validation.Tests;

public sealed class IbanValidatorTests
{
    // Published, well-known sample IBANs (ISO/SWIFT documentation, Wikipedia "IBAN" article) —
    // covers 5 distinct lengths across 6 countries: NL=18, CH=21, GB=22, DE=22, TR=26, FR=27.
    [Theory]
    [InlineData("GB29NWBK60161331926819")]
    [InlineData("GB29 NWBK 6016 1331 9268 19")]
    [InlineData("DE89370400440532013000")]
    [InlineData("FR1420041010050500013M02606")]
    [InlineData("CH9300762011623852957")]
    [InlineData("TR330006100519786457841326")]
    [InlineData("NL91ABNA0417164300")]
    public void Validate_KnownGoodIban_Succeeds(string iban)
    {
        Assert.True(IbanValidator.IsValid(iban));

        var result = IbanValidator.Validate(iban);
        Assert.True(result.IsSuccess);
    }

    [Theory]
    [InlineData("GB")] // too short to even carry check digits
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Validate_MalformedInput_ReturnsInvalidFormat(string? iban)
    {
        var result = IbanValidator.Validate(iban);

        Assert.True(result.IsFailure);
        Assert.Equal(ValidationErrorCodes.Iban.InvalidFormat, result.Error.Code);
    }

    [Fact]
    public void Validate_UnknownCountryPrefix_ReturnsInvalidFormat()
    {
        var result = IbanValidator.Validate("ZZ99123456789012345678");

        Assert.True(result.IsFailure);
        Assert.Equal(ValidationErrorCodes.Iban.InvalidFormat, result.Error.Code);
    }

    [Theory]
    [InlineData("DE8937040044053201300")]     // one character short of DE's registered length (22)
    [InlineData("DE893704004405320130000")]   // one character over
    public void Validate_WrongLengthForCountry_ReturnsInvalidLength(string iban)
    {
        var result = IbanValidator.Validate(iban);

        Assert.True(result.IsFailure);
        Assert.Equal(ValidationErrorCodes.Iban.InvalidLength, result.Error.Code);
    }

    [Theory]
    [InlineData("DE89370400440532013001")] // valid DE IBAN with last digit mutated
    [InlineData("GB29NWBK60161331926818")] // valid GB IBAN with last digit mutated
    [InlineData("CH9300762011623852958")]  // valid CH IBAN with last digit mutated
    public void Validate_CorruptedCheckDigits_ReturnsInvalidCheckDigit(string iban)
    {
        var result = IbanValidator.Validate(iban);

        Assert.True(result.IsFailure);
        Assert.Equal(ValidationErrorCodes.Iban.InvalidCheckDigit, result.Error.Code);
    }

    [Fact]
    public void Validate_ContainsInvalidCharacters_ReturnsInvalidFormat()
    {
        // '!' is neither a letter nor a digit, and is not stripped like spaces/hyphens are.
        var result = IbanValidator.Validate("DE89370400440532013!00");

        Assert.True(result.IsFailure);
        Assert.Equal(ValidationErrorCodes.Iban.InvalidFormat, result.Error.Code);
    }
}
