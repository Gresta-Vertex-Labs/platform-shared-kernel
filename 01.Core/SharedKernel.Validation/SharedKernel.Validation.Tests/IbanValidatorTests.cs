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

    // "ZZ73123456789012345678" is a genuinely mod-97-valid IBAN shape (remainder == 1) under the
    // unrecognized country prefix "ZZ", independently computed via the standard ISO 13616
    // check-digit derivation (rearrange, expand letters, mod 97 == 1). Mutating its last digit to
    // "ZZ73123456789012345670" breaks the checksum (remainder != 1) while keeping the same shape
    // and length — used below to prove the fallback trades away length checking, never checksum
    // correctness.
    private const string Mod97ValidUnknownCountryIban = "ZZ73123456789012345678";
    private const string Mod97InvalidUnknownCountryIban = "ZZ73123456789012345670";

    [Fact]
    public void Validate_UnknownCountryPrefix_DefaultMode_IsUnchangedRegressionCoverage()
    {
        // Regression: even a mod-97-valid value under an unrecognized country still hard-rejects
        // when the caller does not explicitly opt in — proving the new parameter's default of
        // `false` preserves today's exact behavior.
        var implicitDefault = IbanValidator.Validate(Mod97ValidUnknownCountryIban);
        var explicitFalse = IbanValidator.Validate(Mod97ValidUnknownCountryIban, allowFallbackForUnknownCountry: false);

        Assert.True(implicitDefault.IsFailure);
        Assert.Equal(ValidationErrorCodes.Iban.InvalidFormat, implicitDefault.Error.Code);

        Assert.True(explicitFalse.IsFailure);
        Assert.Equal(ValidationErrorCodes.Iban.InvalidFormat, explicitFalse.Error.Code);

        Assert.False(IbanValidator.IsValid(Mod97ValidUnknownCountryIban));
        Assert.False(IbanValidator.IsValid(Mod97ValidUnknownCountryIban, allowFallbackForUnknownCountry: false));
    }

    [Fact]
    public void Validate_UnknownCountryPrefix_FallbackOptedIn_ModValidValue_Succeeds()
    {
        var result = IbanValidator.Validate(Mod97ValidUnknownCountryIban, allowFallbackForUnknownCountry: true);

        Assert.True(result.IsSuccess);
        Assert.True(IbanValidator.IsValid(Mod97ValidUnknownCountryIban, allowFallbackForUnknownCountry: true));
    }

    [Fact]
    public void Validate_UnknownCountryPrefix_FallbackOptedIn_ModInvalidValue_StillRejected()
    {
        // The fallback trades away country-specific length checking, NEVER checksum correctness.
        var result = IbanValidator.Validate(Mod97InvalidUnknownCountryIban, allowFallbackForUnknownCountry: true);

        Assert.True(result.IsFailure);
        Assert.Equal(ValidationErrorCodes.Iban.InvalidCheckDigit, result.Error.Code);

        Assert.False(IbanValidator.IsValid(Mod97InvalidUnknownCountryIban, allowFallbackForUnknownCountry: true));
    }

    [Fact]
    public void Validate_UnknownCountryPrefix_FallbackOptedIn_ExceedsGeneralMaxLength_ReturnsInvalidLength()
    {
        // 35 characters — one over ISO 13616's general 34-character bound — under an unrecognized
        // country prefix. The general-shape bound still applies even in fallback mode.
        string tooLong = "ZZ00" + new string('1', 31);
        Assert.Equal(35, tooLong.Length);

        var result = IbanValidator.Validate(tooLong, allowFallbackForUnknownCountry: true);

        Assert.True(result.IsFailure);
        Assert.Equal(ValidationErrorCodes.Iban.InvalidLength, result.Error.Code);
    }

    [Fact]
    public void RegistryAsOf_IsNonEmpty()
    {
        Assert.False(string.IsNullOrWhiteSpace(IbanValidator.RegistryAsOf));
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
