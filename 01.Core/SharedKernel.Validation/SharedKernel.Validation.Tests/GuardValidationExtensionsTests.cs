using SharedKernel.Guards;
using SharedKernel.Validation.Errors;
using SharedKernel.Validation.Guards;
using SharedKernel.Validation.NationalId;
using Xunit;

namespace SharedKernel.Validation.Tests;

public sealed class GuardValidationExtensionsTests
{
    [Fact]
    public void InvalidIban_PassesForValidIban()
    {
        Assert.Null(Guard.Against.InvalidIban("DE89370400440532013000"));
    }

    [Fact]
    public void InvalidIban_ReturnsMatchingErrorCode_ForInvalidIban()
    {
        var error = Guard.Against.InvalidIban("not-an-iban");

        Assert.NotNull(error);
        Assert.Equal(ValidationErrorCodes.Iban.InvalidFormat, error!.Code);
    }

    [Fact]
    public void InvalidBic_PassesForValidBic()
    {
        Assert.Null(Guard.Against.InvalidBic("DEUTDEFF"));
    }

    [Fact]
    public void InvalidBic_ReturnsMatchingErrorCode_ForInvalidBic()
    {
        var error = Guard.Against.InvalidBic("XX");

        Assert.NotNull(error);
        Assert.Equal(ValidationErrorCodes.Bic.InvalidFormat, error!.Code);
    }

    [Fact]
    public void InvalidPan_PassesForValidPan()
    {
        Assert.Null(Guard.Against.InvalidPan("4242424242424242"));
    }

    [Fact]
    public void InvalidPan_ReturnsMatchingErrorCode_ForInvalidPan()
    {
        var error = Guard.Against.InvalidPan("4242424242424241");

        Assert.NotNull(error);
        Assert.Equal(ValidationErrorCodes.Pan.FailedLuhnCheck, error!.Code);
    }

    [Fact]
    public void InvalidCurrencyCode_PassesForValidCode()
    {
        Assert.Null(Guard.Against.InvalidCurrencyCode("USD"));
    }

    [Fact]
    public void InvalidCurrencyCode_ReturnsMatchingErrorCode_ForInvalidCode()
    {
        var error = Guard.Against.InvalidCurrencyCode("ZZZ");

        Assert.NotNull(error);
        Assert.Equal(ValidationErrorCodes.Currency.UnknownCode, error!.Code);
    }

    [Fact]
    public void InvalidCountryCode_PassesForValidCode()
    {
        Assert.Null(Guard.Against.InvalidCountryCode("US"));
    }

    [Fact]
    public void InvalidCountryCode_ReturnsMatchingErrorCode_ForInvalidCode()
    {
        var error = Guard.Against.InvalidCountryCode("ZZ");

        Assert.NotNull(error);
        Assert.Equal(ValidationErrorCodes.Country.UnknownCode, error!.Code);
    }

    [Fact]
    public void InvalidPhoneNumber_PassesForValidNumber()
    {
        Assert.Null(Guard.Against.InvalidPhoneNumber("+15555550100"));
    }

    [Fact]
    public void InvalidPhoneNumber_ReturnsMatchingErrorCode_ForInvalidNumber()
    {
        var error = Guard.Against.InvalidPhoneNumber("not-a-phone");

        Assert.NotNull(error);
        Assert.Equal(ValidationErrorCodes.Phone.InvalidFormat, error!.Code);
    }

    [Fact]
    public void InvalidVatNumber_PassesForValidNumber()
    {
        Assert.Null(Guard.Against.InvalidVatNumber("DE123456789"));
    }

    [Fact]
    public void InvalidVatNumber_ReturnsMatchingErrorCode_ForInvalidNumber()
    {
        var error = Guard.Against.InvalidVatNumber("123456789");

        Assert.NotNull(error);
        Assert.Equal(ValidationErrorCodes.Vat.InvalidFormat, error!.Code);
    }

    [Fact]
    public void InvalidNationalId_PassesForValidId()
    {
        var registry = new NationalIdValidatorRegistry();

        var error = Guard.Against.InvalidNationalId("10000000146", "TR", registry);

        Assert.Null(error);
    }

    [Fact]
    public void InvalidNationalId_ReturnsInvalidChecksum_ForFailingChecksum()
    {
        var registry = new NationalIdValidatorRegistry();

        var error = Guard.Against.InvalidNationalId("10000000147", "TR", registry);

        Assert.NotNull(error);
        Assert.Equal(ValidationErrorCodes.NationalId.InvalidChecksum, error!.Code);
    }

    [Fact]
    public void InvalidNationalId_ReturnsUnknownCountry_ForUnregisteredCountry()
    {
        var registry = new NationalIdValidatorRegistry();

        var error = Guard.Against.InvalidNationalId("10000000146", "XX", registry);

        Assert.NotNull(error);
        Assert.Equal(ValidationErrorCodes.NationalId.UnknownCountry, error!.Code);
    }

    [Fact]
    public void InvalidNationalId_ReturnsInvalidChecksum_ForNullOrWhitespaceValue()
    {
        var registry = new NationalIdValidatorRegistry();

        var error = Guard.Against.InvalidNationalId("   ", "TR", registry);

        Assert.NotNull(error);
        Assert.Equal(ValidationErrorCodes.NationalId.InvalidChecksum, error!.Code);
    }

    [Fact]
    public void InvalidLei_PassesForValidLei()
    {
        Assert.Null(Guard.Against.InvalidLei("506700GE1G29325QX363"));
    }

    [Fact]
    public void InvalidLei_ReturnsMatchingErrorCode_ForInvalidLei()
    {
        var error = Guard.Against.InvalidLei("not-an-lei");

        Assert.NotNull(error);
        Assert.Equal(ValidationErrorCodes.Lei.InvalidFormat, error!.Code);
    }

    [Fact]
    public void InvalidAbaRoutingNumber_PassesForValidRoutingNumber()
    {
        Assert.Null(Guard.Against.InvalidAbaRoutingNumber("111000025"));
    }

    [Fact]
    public void InvalidAbaRoutingNumber_ReturnsMatchingErrorCode_ForInvalidRoutingNumber()
    {
        var error = Guard.Against.InvalidAbaRoutingNumber("111000024");

        Assert.NotNull(error);
        Assert.Equal(ValidationErrorCodes.AbaRoutingNumber.FailedChecksum, error!.Code);
    }

    [Fact]
    public void InvalidSepaCreditorIdentifier_PassesForValidCreditorIdentifier()
    {
        Assert.Null(Guard.Against.InvalidSepaCreditorIdentifier("DE98ZZZ09999999999"));
    }

    [Fact]
    public void InvalidSepaCreditorIdentifier_ReturnsMatchingErrorCode_ForInvalidCreditorIdentifier()
    {
        var error = Guard.Against.InvalidSepaCreditorIdentifier("DE99ZZZ09999999999");

        Assert.NotNull(error);
        Assert.Equal(ValidationErrorCodes.SepaCreditorIdentifier.InvalidCheckDigit, error!.Code);
    }
}
