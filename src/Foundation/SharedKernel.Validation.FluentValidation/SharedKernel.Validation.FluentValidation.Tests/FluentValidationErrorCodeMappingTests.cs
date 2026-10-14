using FluentValidation;
using SharedKernel.Primitives.Errors;
using Xunit;

namespace SharedKernel.Validation.FluentValidation.Tests;

/// <summary>
/// FluentValidation names a failure after the built-in validator that raised it. The request validator maps those
/// PascalCase names to kernel codes (C1) and passes a code that already follows the format through unchanged. The
/// cases run the real FluentValidation rules, so a renamed built-in validator in a future version fails here.
/// </summary>
public sealed class FluentValidationErrorCodeMappingTests
{
    private sealed record Request(string? Text, int Number, string? Other, string? Missing = null);

    private static readonly (string Rule, Action<InlineValidator<Request>> Configure, string Expected)[] Cases =
    [
        ("NotEmpty", v => v.RuleFor(x => x.Text).NotEmpty(), ErrorCodes.Validation.Required),
        ("NotNull", v => v.RuleFor(x => x.Missing).NotNull(), ErrorCodes.Validation.Required),
        ("MaximumLength", v => v.RuleFor(x => x.Other).MaximumLength(1), ErrorCodes.Validation.MaxLength),
        ("MinimumLength", v => v.RuleFor(x => x.Text).MinimumLength(50), ErrorCodes.Validation.MinLength),
        ("Length", v => v.RuleFor(x => x.Text).Length(40, 50), ErrorCodes.Validation.OutOfRange),
        ("ExactLength", v => v.RuleFor(x => x.Text).Length(40), ErrorCodes.Validation.OutOfRange),
        ("InclusiveBetween", v => v.RuleFor(x => x.Number).InclusiveBetween(1, 2), ErrorCodes.Validation.OutOfRange),
        ("ExclusiveBetween", v => v.RuleFor(x => x.Number).ExclusiveBetween(1, 2), ErrorCodes.Validation.OutOfRange),
        ("GreaterThan", v => v.RuleFor(x => x.Number).GreaterThan(100), ErrorCodes.Validation.OutOfRange),
        ("GreaterThanOrEqualTo", v => v.RuleFor(x => x.Number).GreaterThanOrEqualTo(100), ErrorCodes.Validation.OutOfRange),
        ("LessThan", v => v.RuleFor(x => x.Number).LessThan(0), ErrorCodes.Validation.OutOfRange),
        ("LessThanOrEqualTo", v => v.RuleFor(x => x.Number).LessThanOrEqualTo(0), ErrorCodes.Validation.OutOfRange),
        ("EmailAddress", v => v.RuleFor(x => x.Other).EmailAddress(), ErrorCodes.Validation.InvalidFormat),
        ("Matches", v => v.RuleFor(x => x.Other).Matches("^[0-9]+$"), ErrorCodes.Validation.InvalidFormat),
        ("CreditCard", v => v.RuleFor(x => x.Other).CreditCard(), ErrorCodes.Validation.InvalidFormat),
        ("Must", v => v.RuleFor(x => x.Text).Must(_ => false), ErrorCodes.Validation.Failed),
        ("Equal", v => v.RuleFor(x => x.Text).Equal("other"), ErrorCodes.Validation.Failed),
        ("Empty", v => v.RuleFor(x => x.Other).Empty(), ErrorCodes.Validation.Failed),
        ("CustomPascalCase", v => v.RuleFor(x => x.Text).NotEmpty().WithErrorCode("OrderTooBig"), ErrorCodes.Validation.Failed),
        ("CustomKernelCode", v => v.RuleFor(x => x.Text).NotEmpty().WithErrorCode("order.too_big"), "order.too_big"),
        ("KernelCodeFromErrorCodes", v => v.RuleFor(x => x.Text).NotEmpty().WithErrorCode(ErrorCodes.Conflict.Duplicate), ErrorCodes.Conflict.Duplicate),
    ];

    [Fact]
    public async Task EveryCase_MapsToTheDecidedKernelCode_AsAValidationError()
    {
        foreach ((string rule, Action<InlineValidator<Request>> configure, string expected) in Cases)
        {
            var inline = new InlineValidator<Request>();
            configure(inline);
            var validator = new FluentValidationRequestValidator<Request>([inline]);

            IReadOnlyList<Error> errors = await validator.ValidateAsync(new Request(string.Empty, 5, "abc-def!"), CancellationToken.None);

            Assert.True(errors.Count == 1, $"{rule}: expected one error but got {errors.Count}.");
            Error error = errors[0];
            Assert.True(expected == error.Code, $"{rule}: expected '{expected}' but got '{error.Code}'.");
            Assert.Equal(ErrorType.Validation, error.Type);
        }
    }

    [Fact]
    public async Task ABuiltInValidatorName_IsNeverKept()
    {
        var inline = new InlineValidator<Request>();
        inline.RuleFor(x => x.Text).NotEmpty();
        var validator = new FluentValidationRequestValidator<Request>([inline]);

        IReadOnlyList<Error> errors = await validator.ValidateAsync(new Request(null, 0, null), CancellationToken.None);

        Assert.DoesNotContain(errors, e => e.Code.Contains("Validator", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AFailureWithoutACode_IsAGenericValidationFailure()
    {
        var inline = new InlineValidator<Request>();
        inline.RuleFor(x => x.Text).Custom((_, context) => context.AddFailure("Something is wrong."));
        var validator = new FluentValidationRequestValidator<Request>([inline]);

        IReadOnlyList<Error> errors = await validator.ValidateAsync(new Request("x", 0, null), CancellationToken.None);

        Error error = Assert.Single(errors);
        Assert.Equal(ErrorCodes.Validation.Failed, error.Code);
        Assert.Equal(ErrorType.Validation, error.Type);
    }
}
