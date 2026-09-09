using FluentValidation;
using FluentValidation.Results;
using SharedKernel.Validation.NationalId;
using Xunit;

namespace SharedKernel.Validation.FluentValidation.Tests;

/// <summary>
/// Compiles the exact code sample shown in the 01.Core README's
/// "SharedKernel.Validation.FluentValidation" section and this package's own README, so a
/// documentation drift is caught by the build rather than trusted on sight.
/// </summary>
public sealed class ReadmeSampleCompileTests
{
    private sealed record CreatePaymentCommand(
        string Iban,
        string Bic,
        string CurrencyCode,
        string PayerNationalId,
        string PayerCountryCode);

    private sealed class CreatePaymentCommandValidator : AbstractValidator<CreatePaymentCommand>
    {
        public CreatePaymentCommandValidator(INationalIdValidatorRegistry nationalIdRegistry)
        {
            RuleFor(x => x.Iban).MustBeValidIban();
            RuleFor(x => x.Bic).MustBeValidBic();
            RuleFor(x => x.CurrencyCode).MustBeValidCurrencyCode();
            RuleFor(x => x.PayerNationalId)
                .MustBeValidNationalId(x => x.PayerCountryCode, nationalIdRegistry);
        }
    }

    [Fact]
    public void ReadmeSample_AllRulesValid_Passes()
    {
        var registry = new NationalIdValidatorRegistry();
        var validator = new CreatePaymentCommandValidator(registry);

        var command = new CreatePaymentCommand(
            Iban: "DE89370400440532013000",
            Bic: "DEUTDEFF",
            CurrencyCode: "USD",
            PayerNationalId: "10000000146",
            PayerCountryCode: "TR");

        ValidationResult result = validator.Validate(command);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ReadmeSample_EveryRuleInvalid_FailsOncePerProperty()
    {
        var registry = new NationalIdValidatorRegistry();
        var validator = new CreatePaymentCommandValidator(registry);

        var command = new CreatePaymentCommand(
            Iban: "not-an-iban",
            Bic: "INVALID",
            CurrencyCode: "ZZZ",
            PayerNationalId: "00000000000",
            PayerCountryCode: "TR");

        ValidationResult result = validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Equal(4, result.Errors.Count);
    }

    private sealed record OnboardCounterpartyCommand(string Lei, string AbaRoutingNumber, string SepaCreditorIdentifier);

    private sealed class OnboardCounterpartyCommandValidator : AbstractValidator<OnboardCounterpartyCommand>
    {
        public OnboardCounterpartyCommandValidator()
        {
            RuleFor(x => x.Lei).MustBeValidLei();
            RuleFor(x => x.AbaRoutingNumber).MustBeValidAbaRoutingNumber();
            RuleFor(x => x.SepaCreditorIdentifier).MustBeValidSepaCreditorIdentifier();
        }
    }

    [Fact]
    public void ReadmeSample_LeiAbaRoutingNumberAndSepaCreditorIdentifierRules_AllValid_Passes()
    {
        var validator = new OnboardCounterpartyCommandValidator();

        var command = new OnboardCounterpartyCommand(
            Lei: "506700GE1G29325QX363",
            AbaRoutingNumber: "111000025",
            SepaCreditorIdentifier: "DE98ZZZ09999999999");

        ValidationResult result = validator.Validate(command);

        Assert.True(result.IsValid);
    }
}
