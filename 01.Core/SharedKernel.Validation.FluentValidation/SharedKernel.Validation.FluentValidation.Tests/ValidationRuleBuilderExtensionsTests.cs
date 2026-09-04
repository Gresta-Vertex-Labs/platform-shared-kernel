using FluentValidation;
using FluentValidation.Results;
using SharedKernel.Validation.Errors;
using SharedKernel.Validation.NationalId;
using Xunit;
using StandaloneIbanValidator = SharedKernel.Validation.Validators.IbanValidator;

namespace SharedKernel.Validation.FluentValidation.Tests;

public sealed class ValidationRuleBuilderExtensionsTests
{
    private sealed record Subject(
        string Iban,
        string Bic,
        string Pan,
        string CurrencyCode,
        string CountryCode,
        string PhoneNumber,
        string VatNumber,
        string NationalId,
        string NationalIdCountryCode);

    private sealed class IbanValidator : AbstractValidator<Subject>
    {
        public IbanValidator() => RuleFor(x => x.Iban).MustBeValidIban();
    }

    private sealed class BicValidator : AbstractValidator<Subject>
    {
        public BicValidator() => RuleFor(x => x.Bic).MustBeValidBic();
    }

    private sealed class PanValidator : AbstractValidator<Subject>
    {
        public PanValidator() => RuleFor(x => x.Pan).MustBeValidPan();
    }

    private sealed class CurrencyCodeValidator : AbstractValidator<Subject>
    {
        public CurrencyCodeValidator() => RuleFor(x => x.CurrencyCode).MustBeValidCurrencyCode();
    }

    private sealed class CountryCodeValidator : AbstractValidator<Subject>
    {
        public CountryCodeValidator() => RuleFor(x => x.CountryCode).MustBeValidCountryCode();
    }

    private sealed class PhoneNumberValidator : AbstractValidator<Subject>
    {
        public PhoneNumberValidator() => RuleFor(x => x.PhoneNumber).MustBeValidPhoneNumber();
    }

    private sealed class VatNumberValidator : AbstractValidator<Subject>
    {
        public VatNumberValidator() => RuleFor(x => x.VatNumber).MustBeValidVatNumber();
    }

    private sealed class NationalIdValidator : AbstractValidator<Subject>
    {
        public NationalIdValidator(INationalIdValidatorRegistry registry) =>
            RuleFor(x => x.NationalId).MustBeValidNationalId(x => x.NationalIdCountryCode, registry);
    }

    private static Subject ValidSubject() => new(
        Iban: "DE89370400440532013000",
        Bic: "DEUTDEFF",
        Pan: "4111111111111111",
        CurrencyCode: "USD",
        CountryCode: "DE",
        PhoneNumber: "+14155550100",
        VatNumber: "DE123456789",
        NationalId: "10000000146",
        NationalIdCountryCode: "TR");

    // ---- IBAN -------------------------------------------------------------------------------

    [Fact]
    public void MustBeValidIban_ValidValue_Passes()
    {
        ValidationResult result = new IbanValidator().Validate(ValidSubject());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void MustBeValidIban_MalformedValue_FailsWithInvalidFormatCode()
    {
        ValidationResult result = new IbanValidator().Validate(ValidSubject() with { Iban = "not-an-iban" });

        AssertSingleFailure(result, ValidationErrorCodes.Iban.InvalidFormat);
    }

    [Fact]
    public void MustBeValidIban_WrongLengthForCountry_FailsWithInvalidLengthCode()
    {
        // "DE" requires exactly 22 characters; this is 21 — same country prefix, same overall
        // shape, but the wrong length. Proves the adapter propagates the SPECIFIC code the
        // standalone validator produced, not a single rule-fixed code.
        ValidationResult result = new IbanValidator().Validate(ValidSubject() with { Iban = "DE8937040044053201300" });

        AssertSingleFailure(result, ValidationErrorCodes.Iban.InvalidLength);

        // Parity check against the standalone SharedKernel.Validation call.
        SharedKernel.Primitives.Results.Result standalone =
            StandaloneIbanValidator.Validate("DE8937040044053201300");
        Assert.Equal(standalone.Error.Code, result.Errors[0].ErrorCode);
    }

    [Fact]
    public void MustBeValidIban_RightLengthWrongCheckDigits_FailsWithInvalidCheckDigitCode()
    {
        // Same length/shape as a valid German IBAN, but the check digits are wrong.
        ValidationResult result = new IbanValidator().Validate(ValidSubject() with { Iban = "DE00370400440532013000" });

        AssertSingleFailure(result, ValidationErrorCodes.Iban.InvalidCheckDigit);
    }

    // ---- BIC ----------------------------------------------------------------------------------

    [Fact]
    public void MustBeValidBic_ValidValue_Passes() =>
        Assert.True(new BicValidator().Validate(ValidSubject()).IsValid);

    [Fact]
    public void MustBeValidBic_InvalidValue_FailsWithInvalidFormatCode()
    {
        ValidationResult result = new BicValidator().Validate(ValidSubject() with { Bic = "INVALID" });

        AssertSingleFailure(result, ValidationErrorCodes.Bic.InvalidFormat);
    }

    // ---- PAN ----------------------------------------------------------------------------------

    [Fact]
    public void MustBeValidPan_ValidValue_Passes() =>
        Assert.True(new PanValidator().Validate(ValidSubject()).IsValid);

    [Fact]
    public void MustBeValidPan_FailsLuhnCheck_FailsWithFailedLuhnCheckCode()
    {
        ValidationResult result = new PanValidator().Validate(ValidSubject() with { Pan = "4111111111111112" });

        AssertSingleFailure(result, ValidationErrorCodes.Pan.FailedLuhnCheck);
    }

    // ---- Currency code ------------------------------------------------------------------------

    [Fact]
    public void MustBeValidCurrencyCode_ValidValue_Passes() =>
        Assert.True(new CurrencyCodeValidator().Validate(ValidSubject()).IsValid);

    [Fact]
    public void MustBeValidCurrencyCode_UnknownCode_FailsWithUnknownCodeCode()
    {
        ValidationResult result = new CurrencyCodeValidator().Validate(ValidSubject() with { CurrencyCode = "ZZZ" });

        AssertSingleFailure(result, ValidationErrorCodes.Currency.UnknownCode);
    }

    // ---- Country code ---------------------------------------------------------------------------

    [Fact]
    public void MustBeValidCountryCode_ValidValue_Passes() =>
        Assert.True(new CountryCodeValidator().Validate(ValidSubject()).IsValid);

    [Fact]
    public void MustBeValidCountryCode_UnknownCode_FailsWithUnknownCodeCode()
    {
        ValidationResult result = new CountryCodeValidator().Validate(ValidSubject() with { CountryCode = "ZZ" });

        AssertSingleFailure(result, ValidationErrorCodes.Country.UnknownCode);
    }

    // ---- Phone number -------------------------------------------------------------------------

    [Fact]
    public void MustBeValidPhoneNumber_ValidValue_Passes() =>
        Assert.True(new PhoneNumberValidator().Validate(ValidSubject()).IsValid);

    [Fact]
    public void MustBeValidPhoneNumber_InvalidValue_FailsWithInvalidFormatCode()
    {
        ValidationResult result = new PhoneNumberValidator().Validate(ValidSubject() with { PhoneNumber = "0123" });

        AssertSingleFailure(result, ValidationErrorCodes.Phone.InvalidFormat);
    }

    // ---- VAT number -----------------------------------------------------------------------------

    [Fact]
    public void MustBeValidVatNumber_ValidValue_Passes() =>
        Assert.True(new VatNumberValidator().Validate(ValidSubject()).IsValid);

    [Fact]
    public void MustBeValidVatNumber_InvalidValue_FailsWithInvalidFormatCode()
    {
        ValidationResult result = new VatNumberValidator().Validate(ValidSubject() with { VatNumber = "!" });

        AssertSingleFailure(result, ValidationErrorCodes.Vat.InvalidFormat);
    }

    // ---- National ID ----------------------------------------------------------------------------

    [Fact]
    public void MustBeValidNationalId_ValidValueForRegisteredCountry_Passes()
    {
        var registry = new NationalIdValidatorRegistry();

        Assert.True(new NationalIdValidator(registry).Validate(ValidSubject()).IsValid);
    }

    [Fact]
    public void MustBeValidNationalId_FailingChecksum_FailsWithInvalidChecksumCode()
    {
        var registry = new NationalIdValidatorRegistry();
        Subject subject = ValidSubject() with { NationalId = "10000000147" }; // TCKN with a corrupted last digit

        ValidationResult result = new NationalIdValidator(registry).Validate(subject);

        AssertSingleFailure(result, ValidationErrorCodes.NationalId.InvalidChecksum);
    }

    [Fact]
    public void MustBeValidNationalId_UnregisteredCountry_FailsWithUnknownCountryCode()
    {
        var registry = new NationalIdValidatorRegistry();
        Subject subject = ValidSubject() with { NationalIdCountryCode = "ZZ" };

        ValidationResult result = new NationalIdValidator(registry).Validate(subject);

        AssertSingleFailure(result, ValidationErrorCodes.NationalId.UnknownCountry);
    }

    // ---- Null-argument guards -------------------------------------------------------------------

    [Fact]
    public void MustBeValidNationalId_NullCountryCodeSelector_Throws()
    {
        var registry = new NationalIdValidatorRegistry();

        Assert.Throws<ArgumentNullException>(() =>
            new InlineValidator<Subject>().RuleFor(x => x.NationalId).MustBeValidNationalId(null!, registry));
    }

    [Fact]
    public void MustBeValidNationalId_NullRegistry_Throws() =>
        Assert.Throws<ArgumentNullException>(() =>
            new InlineValidator<Subject>().RuleFor(x => x.NationalId).MustBeValidNationalId(x => x.NationalIdCountryCode, null!));

    private static void AssertSingleFailure(ValidationResult result, string expectedErrorCode)
    {
        Assert.False(result.IsValid);
        ValidationFailure failure = Assert.Single(result.Errors);
        Assert.Equal(expectedErrorCode, failure.ErrorCode);
        Assert.False(string.IsNullOrWhiteSpace(failure.ErrorMessage));
    }
}
