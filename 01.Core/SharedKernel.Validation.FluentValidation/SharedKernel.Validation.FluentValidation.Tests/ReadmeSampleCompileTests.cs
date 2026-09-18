using FluentValidation;
using FluentValidation.Results;
using Xunit;

namespace SharedKernel.Validation.FluentValidation.Tests;

/// <summary>
/// Runs the code shown in this package's README and recipe 1 of <c>SharedKernel.Validation</c>'s
/// README, so a sample that stops compiling or behaving as described fails here.
/// </summary>
public sealed class ReadmeSampleCompileTests
{
    public sealed record CreateCustomer(string Iban, string? Bic, string Country, string? TaxNumber, string? NationalId, string? Card);

    public sealed class CreateCustomerValidator : AbstractValidator<CreateCustomer>
    {
        public CreateCustomerValidator()
        {
            RuleFor(x => x.Iban).NotEmpty().MustBeValidIban();
            RuleFor(x => x.Bic).MustBeValidBic();
            RuleFor(x => x.Country).NotEmpty().MustBeValidCountryCode();
            RuleFor(x => x.TaxNumber).MustBeValidVatNumber(x => x.Country);
            RuleFor(x => x.NationalId).MustBeValidNationalId(x => x.Country);
            RuleFor(x => x.Card).MustBeValidCardNumber(requireKnownNetwork: true);
        }
    }

    private static readonly CreateCustomerValidator Validator = new();

    [Fact]
    public void Usage_ValidTurkishCustomer_Passes()
    {
        var customer = new CreateCustomer("TR330006100519786457841326", null, "TR", "4540536920", "10000000146", null);

        Assert.True(Validator.Validate(customer).IsValid);
    }

    [Fact]
    public void WhatAFailureLooksLike()
    {
        var customer = new CreateCustomer("DE8937040044053201300", null, "DE", null, null, null);

        ValidationFailure failure = Assert.Single(Validator.Validate(customer).Errors);

        Assert.Equal("Iban", failure.PropertyName);
        Assert.Equal("validation.iban.invalid_length", failure.ErrorCode);
        Assert.Equal("An IBAN from DE is 22 characters long, not 21.", failure.ErrorMessage);
        Assert.Equal("DE", failure.FormattedMessagePlaceholderValues["country"]);
        Assert.Equal(22, failure.FormattedMessagePlaceholderValues["expected"]);
        Assert.Equal(21, failure.FormattedMessagePlaceholderValues["actual"]);
        Assert.Null(failure.AttemptedValue);
    }

    [Fact]
    public void Pitfalls_WithNameOnAnEarlierRule_NamesTheField()
    {
        var validator = new InlineValidator<CreateCustomer>();
        validator.RuleFor(x => x.Bic).NotEmpty().WithName("Bank code").MustBeValidBic();

        ValidationFailure failure = Assert.Single(validator.Validate(new CreateCustomer("x", "bad", "TR", null, null, null)).Errors);

        Assert.Equal("Bank code", failure.FormattedMessagePlaceholderValues["PropertyName"]);
    }

    public sealed record Account(string Iban, string Currency);

    public sealed record Customer(string? Iban, string? Bic, string PaymentMethod, List<Account> Accounts);

    [Fact]
    public void Recipe1_RequiredVersusOptional()
    {
        var validator = new InlineValidator<Customer>();
        validator.RuleFor(x => x.Iban).NotEmpty().MustBeValidIban();
        validator.RuleFor(x => x.Bic).MustBeValidBic();

        Assert.Equal("NotEmptyValidator", Assert.Single(validator.Validate(new Customer(null, null, "card", [])).Errors).ErrorCode);
        Assert.Equal(
            "validation.required",
            Assert.Single(validator.Validate(new Customer("DE89370400440532013000", "", "card", [])).Errors).ErrorCode);
    }

    [Fact]
    public void Recipe2_ACollectionOfAccounts_ReportsTheIndexedPath()
    {
        var validator = new InlineValidator<Customer>();
        validator.RuleForEach(x => x.Accounts).ChildRules(account =>
        {
            account.RuleFor(a => a.Iban).NotEmpty().MustBeValidIban();
            account.RuleFor(a => a.Currency).NotEmpty().MustBeValidCurrencyCode();
        });

        var customer = new Customer(null, null, "card", [new("DE89370400440532013000", "EUR"), new("DE88370400440532013000", "EUR")]);

        Assert.Equal("Accounts[1].Iban", Assert.Single(validator.Validate(customer).Errors).PropertyName);
    }

    [Fact]
    public void Recipe3_OnlyCheckInSomeCases()
    {
        var validator = new InlineValidator<Customer>();
        validator.RuleFor(x => x.Iban).MustBeValidIban().When(x => x.PaymentMethod == "transfer");

        Assert.True(validator.Validate(new Customer("bad", null, "card", [])).IsValid);
        Assert.False(validator.Validate(new Customer("bad", null, "transfer", [])).IsValid);
    }

    public sealed record ReturnRequest(string OrderNumber);

    [Fact]
    public void Recipe4_YourOwnIdentifierType()
    {
        var validator = new InlineValidator<ReturnRequest>();
        validator.RuleFor(x => x.OrderNumber).MustBeValid<ReturnRequest, OrderNumber>();

        Assert.True(validator.Validate(new ReturnRequest("ORD1234567")).IsValid);
        Assert.Equal("order_number.invalid_format", Assert.Single(validator.Validate(new ReturnRequest("X")).Errors).ErrorCode);
        Assert.Equal("ORD1234567", OrderNumber.Parse("ORD1234567", null).Value);
    }

    [Fact]
    public void Recipe5_AssertAFailureInAUnitTest()
    {
        var customer = new CreateCustomer("DE8937040044053201300", null, "DE", null, null, null);

        ValidationFailure failure = Assert.Single(new CreateCustomerValidator().Validate(customer).Errors);

        Assert.Equal(ValidationErrorCodes.Iban.InvalidLength, failure.ErrorCode);
        Assert.Equal(22, failure.FormattedMessagePlaceholderValues["expected"]);
    }
}

public readonly record struct OrderNumber : IValidatedValue<OrderNumber>
{
    private OrderNumber(string value) => Value = value;

    public string Value { get; }

    public static SharedKernel.Primitives.Results.Result<OrderNumber> Create(string? value) =>
        value is { Length: 10 } && value.StartsWith("ORD", StringComparison.Ordinal) && value[3..].All(char.IsAsciiDigit)
            ? new OrderNumber(value)
            : SharedKernel.Primitives.Errors.Error.Validation("order_number.invalid_format", "An order number is ORD followed by 7 digits.");

    public static OrderNumber Parse(string s, IFormatProvider? provider) =>
        Create(s) is { IsSuccess: true } ok ? ok.Value : throw new FormatException("Invalid order number.");

    public static bool TryParse(string? s, IFormatProvider? provider, out OrderNumber result)
    {
        SharedKernel.Primitives.Results.Result<OrderNumber> created = Create(s);
        result = created.IsSuccess ? created.Value : default;
        return created.IsSuccess;
    }
}
