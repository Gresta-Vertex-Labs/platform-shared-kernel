using FluentValidation;
using FluentValidation.Results;
using SharedKernel.Primitives.Errors;
using SharedKernel.Validation.Errors;
using Xunit;

namespace SharedKernel.Validation.FluentValidation.Tests;

/// <summary>
/// Proves that a <c>.MustBeValid*()</c> rule composes into a MediatR-pipeline-shaped validation
/// harness with zero extra plumbing, exactly as promised by <see cref="ValidationRuleBuilderExtensions"/>'s
/// XML docs.
/// </summary>
/// <remarks>
/// This deliberately does NOT take a live reference to <c>05.Application.Behaviors</c> or
/// <c>16.Testing</c> — both are out of this package's dependency graph by design (WO-067). Instead
/// <see cref="LocalValidationPipelineHarness"/> is a local, hand-written stand-in reproducing
/// <c>05.Application.Behaviors.Validation.ValidationBehavior&lt;TRequest,TResponse&gt;</c>'s exact
/// aggregation shape as of this writing — read directly from that type's source
/// (05.Application/SharedKernel.Application.Behaviors/Validation/ValidationBehavior.cs): run every
/// registered <c>IValidator&lt;TRequest&gt;</c>, collect every <see cref="ValidationFailure"/>
/// across all of them, and project each one via <c>Error.Validation(failure.PropertyName, failure.ErrorMessage)</c>
/// — throwing (there, <c>SharedKernel.Core.Exceptions.ValidationException</c>; here, a local
/// exception type) when the aggregate is non-empty. What this test proves is genuine: that FluentValidation
/// rules built by this package's extension methods run and fail exactly like any other FluentValidation
/// rule when driven through that exact aggregation shape, so a service wiring <c>.MustBeValidIban()</c>
/// into an <c>AbstractValidator&lt;TCommand&gt;</c> that already participates in
/// <c>ValidationBehavior</c> needs no adapter, wrapper, or extra registration. It is not a
/// substitute for an integration test against the real <c>ValidationBehavior</c> type, which would
/// require the disallowed cross-domain reference.
/// </remarks>
public sealed class ValidationBehaviorInteropTests
{
    private sealed record CreatePaymentCommand(string Iban, string CurrencyCode);

    private sealed class CreatePaymentCommandValidator : AbstractValidator<CreatePaymentCommand>
    {
        public CreatePaymentCommandValidator()
        {
            RuleFor(x => x.Iban).MustBeValidIban();
            RuleFor(x => x.CurrencyCode).MustBeValidCurrencyCode();
        }
    }

    private sealed class LocalPipelineValidationException(IReadOnlyList<Error> errors) : Exception
    {
        public IReadOnlyList<Error> Errors { get; } = errors;
    }

    /// <summary>
    /// Local stand-in for <c>ValidationBehavior&lt;TRequest,TResponse&gt;.Handle</c>'s validation
    /// step: run every validator, aggregate every failure via <c>Error.Validation(PropertyName, ErrorMessage)</c>,
    /// and throw when the aggregate is non-empty — otherwise invoke <paramref name="next"/>.
    /// </summary>
    private static TResponse RunPipeline<TRequest, TResponse>(
        IEnumerable<IValidator<TRequest>> validators,
        TRequest request,
        Func<TResponse> next)
    {
        List<Error> errors = validators
            .Select(validator => validator.Validate(request))
            .SelectMany(result => result.Errors)
            .Select(failure => Error.Validation(failure.PropertyName, failure.ErrorMessage))
            .ToList();

        if (errors.Count > 0)
        {
            throw new LocalPipelineValidationException(errors);
        }

        return next();
    }

    [Fact]
    public void Pipeline_ValidCommand_InvokesNext()
    {
        var command = new CreatePaymentCommand("DE89370400440532013000", "USD");
        IValidator<CreatePaymentCommand>[] validators = [new CreatePaymentCommandValidator()];

        bool nextInvoked = false;
        string response = RunPipeline<CreatePaymentCommand, string>(validators, command, () =>
        {
            nextInvoked = true;
            return "ok";
        });

        Assert.True(nextInvoked);
        Assert.Equal("ok", response);
    }

    [Fact]
    public void Pipeline_InvalidCommand_ShortCircuitsWithAggregatedErrors_AndNeverInvokesNext()
    {
        var command = new CreatePaymentCommand("not-an-iban", "ZZZ");
        IValidator<CreatePaymentCommand>[] validators = [new CreatePaymentCommandValidator()];

        bool nextInvoked = false;

        LocalPipelineValidationException thrown = Assert.Throws<LocalPipelineValidationException>(() =>
            RunPipeline<CreatePaymentCommand, string>(validators, command, () =>
            {
                nextInvoked = true;
                return "unreachable";
            }));

        Assert.False(nextInvoked);
        Assert.Equal(2, thrown.Errors.Count);
        Assert.All(thrown.Errors, error => Assert.Equal(ErrorType.Validation, error.Type));
    }

    [Fact]
    public void Pipeline_InvalidCommand_UnderlyingFailure_StillCarriesSharedKernelValidationErrorCode()
    {
        // The pipeline harness above projects Error.Code from FluentValidation's PropertyName
        // (mirroring ValidationBehavior's current behavior verbatim). This test proves the
        // finer-grained SharedKernel.Validation code is NOT lost — it is still directly readable
        // off the raw ValidationFailure, for any consumer (or future ValidationBehavior revision)
        // that wants it instead of PropertyName.
        var command = new CreatePaymentCommand("not-an-iban", "USD");
        var validator = new CreatePaymentCommandValidator();

        ValidationResult result = validator.Validate(command);

        ValidationFailure failure = Assert.Single(result.Errors);
        Assert.Equal(nameof(CreatePaymentCommand.Iban), failure.PropertyName);
        Assert.Equal(ValidationErrorCodes.Iban.InvalidFormat, failure.ErrorCode);
    }
}
