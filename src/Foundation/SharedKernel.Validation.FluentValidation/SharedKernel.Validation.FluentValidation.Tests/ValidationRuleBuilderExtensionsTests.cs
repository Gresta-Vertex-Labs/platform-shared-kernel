using FluentValidation;
using FluentValidation.Results;
using SharedKernel.Primitives.Errors;
using Xunit;

namespace SharedKernel.Validation.FluentValidation.Tests;

public sealed class ValidationRuleBuilderExtensionsTests
{
    private sealed record Payment(
        string? Iban,
        string? Bic = null,
        string? Card = null,
        string? Country = null,
        string? Currency = null,
        string? Phone = null,
        string? Lei = null,
        string? Aba = null,
        string? Sepa = null,
        string? Vat = null,
        string? NationalId = null);

    private sealed class PaymentValidator : AbstractValidator<Payment>
    {
        public PaymentValidator()
        {
            RuleFor(x => x.Iban).MustBeValidIban();
            RuleFor(x => x.Bic).MustBeValidBic();
            RuleFor(x => x.Card).MustBeValidCardNumber(requireKnownNetwork: true);
            RuleFor(x => x.Country).MustBeValidCountryCode();
            RuleFor(x => x.Currency).MustBeValidCurrencyCode();
            RuleFor(x => x.Phone).MustBeValidPhoneNumber();
            RuleFor(x => x.Lei).MustBeValidLei();
            RuleFor(x => x.Aba).MustBeValidAbaRoutingNumber();
            RuleFor(x => x.Sepa).MustBeValidSepaCreditorId();
            RuleFor(x => x.Vat).MustBeValidVatNumber(x => x.Country);
            RuleFor(x => x.NationalId).MustBeValidNationalId(x => x.Country);
        }
    }

    private static readonly PaymentValidator Validator = new();

    [Fact]
    public void AllValid_Passes()
    {
        var payment = new Payment(
            "DE89370400440532013000", "DEUTDEFF", "4111111111111111", "TR", "TRY", "+905321234567",
            "5493001KJTIIGC8Y1R12", "011000015", "DE98ZZZ09999999999", "4540536920", "10000000146");

        Assert.True(Validator.Validate(payment).IsValid);
    }

    [Fact]
    public void NullValues_Pass_LikeFluentValidationsOwnFormatRules()
    {
        Assert.True(Validator.Validate(new Payment(Iban: null)).IsValid);
    }

    [Fact]
    public void EmptyString_FailsWithRequired()
    {
        ValidationFailure failure = Assert.Single(Validator.Validate(new Payment(Iban: "")).Errors);

        Assert.Equal(ErrorCodes.Validation.Required, failure.ErrorCode);
    }

    [Fact]
    public void Failure_CarriesTheSpecificCodeMessageAndValues_ButNeverTheRejectedValue()
    {
        ValidationFailure failure = Assert.Single(Validator.Validate(new Payment(Iban: "DE8937040044053201300")).Errors);

        Assert.Equal(ValidationErrorCodes.Iban.InvalidLength, failure.ErrorCode);
        Assert.Equal("An IBAN from DE is 22 characters long, not 21.", failure.ErrorMessage);
        Assert.Equal("Iban", failure.PropertyName);
        Assert.Null(failure.AttemptedValue);
        Assert.Equal("DE", failure.FormattedMessagePlaceholderValues["country"]);
        Assert.Equal(22, failure.FormattedMessagePlaceholderValues["expected"]);
        Assert.Equal("Iban", failure.FormattedMessagePlaceholderValues[ErrorArgumentNames.PropertyPath]);
        Assert.Equal("Iban", failure.FormattedMessagePlaceholderValues[ErrorArgumentNames.PropertyName]);
        Assert.False(failure.FormattedMessagePlaceholderValues.ContainsKey("PropertyValue"));
        Assert.Equal(ValidationErrorCodes.Iban.InvalidLength, Assert.IsType<Error>(failure.CustomState).Code);
    }

    [Fact]
    public void CardNumberFailure_DoesNotExposeTheNumberAnywhere()
    {
        ValidationFailure failure = Assert.Single(Validator.Validate(new Payment(Iban: null, Card: "4111111111111112")).Errors);

        Assert.Equal(ValidationErrorCodes.CardNumber.InvalidCheckDigit, failure.ErrorCode);
        Assert.Null(failure.AttemptedValue);
        Assert.DoesNotContain("4111", failure.ErrorMessage, StringComparison.Ordinal);
        Assert.DoesNotContain(failure.FormattedMessagePlaceholderValues.Values, v => v?.ToString()?.Contains("4111", StringComparison.Ordinal) == true);
    }

    [Theory]
    [InlineData("TR", "4540536921", ValidationErrorCodes.VatNumber.InvalidCheckDigit)]
    [InlineData("DE", "4540536920", ValidationErrorCodes.VatNumber.InvalidFormat)]
    public void VatNumber_UsesTheCountryFromTheOtherProperty(string country, string vat, string code)
    {
        ValidationFailure failure = Assert.Single(Validator.Validate(new Payment(Iban: null, Country: country, Vat: vat)).Errors);

        Assert.Equal(code, failure.ErrorCode);
    }

    [Fact]
    public void CountryDependentRules_AreSkipped_WhenTheCountryIsMissingOrInvalid()
    {
        ValidationResult result = Validator.Validate(new Payment(Iban: null, Country: "QQ", Vat: "garbage", NationalId: "garbage"));

        ValidationFailure failure = Assert.Single(result.Errors);
        Assert.Equal("Country", failure.PropertyName);
    }

    [Fact]
    public void NationalId_UnsupportedCountry_Fails()
    {
        ValidationFailure failure = Assert.Single(Validator.Validate(new Payment(Iban: null, Country: "DE", NationalId: "123")).Errors);

        Assert.Equal(ValidationErrorCodes.NationalId.UnsupportedCountry, failure.ErrorCode);
    }

    [Fact]
    public void When_And_AnEarlierWithName_Work()
    {
        var validator = new InlineValidator<Payment>();
        validator.RuleFor(x => x.Iban).MustBeValidIban().When(x => x.Currency == "EUR");
        validator.RuleFor(x => x.Bic).NotNull().WithName("Bank code").MustBeValidBic();

        Assert.True(validator.Validate(new Payment("bad", Bic: "DEUTDEFF", Currency: "TRY")).IsValid);

        ValidationFailure failure = Assert.Single(validator.Validate(new Payment(Iban: null, Bic: "bad")).Errors);
        Assert.Equal("Bank code", failure.FormattedMessagePlaceholderValues[ErrorArgumentNames.PropertyName]);
    }

    [Fact]
    public void NestedProperty_ReportsTheFullPath()
    {
        var validator = new InlineValidator<Order>();
        validator.RuleForEach(x => x.Payments).ChildRules(p => p.RuleFor(x => x.Iban).MustBeValidIban());

        ValidationFailure failure = Assert.Single(validator.Validate(new Order([new Payment("DE88370400440532013000")])).Errors);

        Assert.Equal("Payments[0].Iban", failure.PropertyName);
        Assert.Equal("Payments[0].Iban", failure.FormattedMessagePlaceholderValues[ErrorArgumentNames.PropertyPath]);
    }

    [Fact]
    public void MustBeValid_WorksForAnyIdentifierType()
    {
        var validator = new InlineValidator<Payment>();
        validator.RuleFor(x => x.Iban).MustBeValid<Payment, Iban>();

        Assert.Equal(
            ValidationErrorCodes.Iban.InvalidCheckDigits,
            Assert.Single(validator.Validate(new Payment("DE88370400440532013000")).Errors).ErrorCode);
    }

    private sealed record Order(List<Payment> Payments);
}
