using SharedKernel.Validation.Errors;
using SharedKernel.Validation.Validators;
using Xunit;

namespace SharedKernel.Validation.Tests;

public sealed class LeiValidatorTests
{
    // Real, published LEI codes (ISO 17442). Sources:
    //  - "506700GE1G29325QX363" is GLEIF's own LEI, published on
    //    https://www.gleif.org/en/about-lei/iso-17442-the-lei-code-structure
    //  - The remaining four (with their LOU prefix / entity-specific reference / check-digit
    //    breakdown) are published on Wikipedia's "Legal Entity Identifier" article's worked
    //    example table.
    // Every vector's ISO/IEC 7064 MOD 97-10 checksum (mod-97 over the full 20-character string,
    // letters expanded A=10..Z=35) was independently recomputed and confirmed to equal 1 before
    // being used here — not merely trusted from the source page.
    [Theory]
    [InlineData("506700GE1G29325QX363")] // GLEIF's own LEI
    [InlineData("54930084UKLVMY22DS16")] // G.E. Financing GmbH
    [InlineData("213800WSGIIZCXF1P572")] // Jaguar Land Rover Ltd
    [InlineData("5493000IBP32UQZ0KL24")] // British Broadcasting Corporation
    [InlineData("L3I9ZG2KFGXZ61BMYR72")] // Bank of Nova Scotia
    public void Validate_KnownGoodLei_Succeeds(string lei)
    {
        Assert.True(LeiValidator.IsValid(lei));

        var result = LeiValidator.Validate(lei);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_LowercaseAndSurroundingWhitespace_IsNormalizedAndSucceeds()
    {
        var result = LeiValidator.Validate("  506700ge1g29325qx363  ");

        Assert.True(result.IsSuccess);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Validate_NullOrEmpty_ReturnsInvalidFormat(string? lei)
    {
        var result = LeiValidator.Validate(lei);

        Assert.True(result.IsFailure);
        Assert.Equal(ValidationErrorCodes.Lei.InvalidFormat, result.Error.Code);
    }

    [Theory]
    [InlineData("506700GE1G29325QX36")]  // 19 characters — one short
    [InlineData("506700GE1G29325QX3633")] // 21 characters — one over
    public void Validate_WrongLength_ReturnsInvalidFormat(string lei)
    {
        var result = LeiValidator.Validate(lei);

        Assert.True(result.IsFailure);
        Assert.Equal(ValidationErrorCodes.Lei.InvalidFormat, result.Error.Code);
    }

    [Fact]
    public void Validate_NonAlphanumericCharacterInFirst18_ReturnsInvalidFormat()
    {
        // Index 5 (originally '0') replaced with '!' — still 20 characters overall.
        var result = LeiValidator.Validate("50670!GE1G29325QX363");

        Assert.True(result.IsFailure);
        Assert.Equal(ValidationErrorCodes.Lei.InvalidFormat, result.Error.Code);
    }

    [Fact]
    public void Validate_NonNumericCheckDigitCharacter_ReturnsInvalidFormat()
    {
        // Last character replaced with a letter — the check digits must be numeric.
        var result = LeiValidator.Validate("506700GE1G29325QX36A");

        Assert.True(result.IsFailure);
        Assert.Equal(ValidationErrorCodes.Lei.InvalidFormat, result.Error.Code);
    }

    [Fact]
    public void Validate_CorruptedCheckDigits_ReturnsInvalidCheckDigit()
    {
        // GLEIF's own LEI with the last digit mutated (363 -> 364); independently confirmed via
        // the published MOD 97-10 algorithm that this no longer yields remainder 1.
        var result = LeiValidator.Validate("506700GE1G29325QX364");

        Assert.True(result.IsFailure);
        Assert.Equal(ValidationErrorCodes.Lei.InvalidCheckDigit, result.Error.Code);
    }
}
