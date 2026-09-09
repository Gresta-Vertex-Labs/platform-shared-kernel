using SharedKernel.Validation.Errors;
using SharedKernel.Validation.Validators;
using Xunit;

namespace SharedKernel.Validation.Tests;

public sealed class SepaCreditorIdentifierValidatorTests
{
    // "DE98ZZZ09999999999" is the widely-published test/example SEPA Creditor Identifier cited by
    // Deutsche Bundesbank and repeated across multiple SEPA implementation references, correctly
    // computed per the European Payments Council's Creditor Identifier Overview
    // (check digits = 98 - mod97(nationalIdentifier + countryCode + "00"), excluding the Creditor
    // Business Code from the checksum input). Independently recomputed and confirmed at
    // implementation time, not merely trusted from a secondary source.
    [Fact]
    public void Validate_PublishedTestCreditorIdentifier_Succeeds()
    {
        var result = SepaCreditorIdentifierValidator.Validate("DE98ZZZ09999999999");

        Assert.True(result.IsSuccess);
        Assert.True(SepaCreditorIdentifierValidator.IsValid("DE98ZZZ09999999999"));
    }

    // The following two vectors are HAND-CONSTRUCTED and independently verified against the
    // published EPC algorithm above (not sourced from a registry) — computed by running
    // checkDigits = 98 - mod97(nationalIdentifier + countryCode + "00") for a chosen country and
    // national identifier, mirroring this package's own P-443 fallback precedent for a vector
    // with no convenient real published example.
    [Theory]
    [InlineData("FR91ZZZ12345678901")]
    [InlineData("NL96ZZZABC1234567")]
    public void Validate_HandConstructedVerifiedCreditorIdentifier_Succeeds(string creditorId)
    {
        var result = SepaCreditorIdentifierValidator.Validate(creditorId);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_LowercaseAndSpaces_IsNormalizedAndSucceeds()
    {
        var result = SepaCreditorIdentifierValidator.Validate("de98 zzz0 9999999999");

        Assert.True(result.IsSuccess);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Validate_NullOrEmpty_ReturnsInvalidFormat(string? creditorId)
    {
        var result = SepaCreditorIdentifierValidator.Validate(creditorId);

        Assert.True(result.IsFailure);
        Assert.Equal(ValidationErrorCodes.SepaCreditorIdentifier.InvalidFormat, result.Error.Code);
    }

    [Fact]
    public void Validate_TooShort_ReturnsInvalidFormat()
    {
        var result = SepaCreditorIdentifierValidator.Validate("DE98");

        Assert.True(result.IsFailure);
        Assert.Equal(ValidationErrorCodes.SepaCreditorIdentifier.InvalidFormat, result.Error.Code);
    }

    [Fact]
    public void Validate_NationalIdentifierOver28Characters_ReturnsInvalidFormat()
    {
        // Business code "ZZZ" + a 29-character national identifier — one over the documented
        // 28-character maximum, pushing the overall length to 36 (over the 35-character cap).
        var result = SepaCreditorIdentifierValidator.Validate("DE98ZZZ" + new string('1', 29));

        Assert.True(result.IsFailure);
        Assert.Equal(ValidationErrorCodes.SepaCreditorIdentifier.InvalidFormat, result.Error.Code);
    }

    [Fact]
    public void Validate_UnrecognizedCountryCode_ReturnsInvalidFormat()
    {
        var result = SepaCreditorIdentifierValidator.Validate("ZZ98ZZZ09999999999");

        Assert.True(result.IsFailure);
        Assert.Equal(ValidationErrorCodes.SepaCreditorIdentifier.InvalidFormat, result.Error.Code);
    }

    [Fact]
    public void Validate_CorruptedCheckDigits_ReturnsInvalidCheckDigit()
    {
        // The published test Creditor Identifier with its check digits mutated (98 -> 99).
        var result = SepaCreditorIdentifierValidator.Validate("DE99ZZZ09999999999");

        Assert.True(result.IsFailure);
        Assert.Equal(ValidationErrorCodes.SepaCreditorIdentifier.InvalidCheckDigit, result.Error.Code);
    }

    [Fact]
    public void Validate_BusinessCodeNeverAffectsCheckDigits()
    {
        // Per the EPC Creditor Identifier Overview, changing only the Creditor Business Code
        // (positions 5-7, "ZZZ" -> "001") must never change the check digits ("98"), since the
        // business code is excluded from the checksum input.
        var withZzz = SepaCreditorIdentifierValidator.Validate("DE98ZZZ09999999999");
        var withOtherBusinessCode = SepaCreditorIdentifierValidator.Validate("DE98001" + "09999999999");

        Assert.True(withZzz.IsSuccess);
        Assert.True(withOtherBusinessCode.IsSuccess);
    }
}
