using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using SharedKernel.Application.Behaviors.Validation;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application.Behaviors.Tests.Validation;

public sealed class ValidationBehaviorTests
{
    private sealed record TestRequest(string Name) : IRequest<Result>;

    private sealed class PassingValidator : AbstractValidator<TestRequest>;

    private sealed class FailingValidator : AbstractValidator<TestRequest>
    {
        public FailingValidator(string propertyName, string message)
        {
            RuleFor(x => x.Name).Custom((_, context) => context.AddFailure(new ValidationFailure(propertyName, message)));
        }
    }

    private sealed class SequentialOrderRecordingValidator : AbstractValidator<TestRequest>
    {
        public SequentialOrderRecordingValidator(List<int> order, int id)
        {
            // If two validators ran concurrently (Task.WhenAll), their Task.Yield-interleaved
            // start order would not reliably match registration order. Sequential execution
            // guarantees this validator fully runs — including the yield — before the next one starts.
            RuleFor(x => x.Name).CustomAsync(async (_, _, _) =>
            {
                order.Add(id);
                await Task.Yield();
            });
        }
    }

    private sealed record Account(string Iban);

    private sealed record TransferRequest(IReadOnlyList<Account> Accounts) : IRequest<Result>;

    private sealed class AccountValidator : AbstractValidator<Account>
    {
        public AccountValidator()
        {
            RuleFor(x => x.Iban)
                .Must(_ => false)
                .WithErrorCode("validation.iban.invalid_check_digits")
                .WithName("IBAN")
                .WithMessage("{PropertyName} check digits are not correct.");
        }
    }

    private sealed class TransferValidator : AbstractValidator<TransferRequest>
    {
        public TransferValidator()
        {
            RuleForEach(x => x.Accounts).SetValidator(new AccountValidator());
        }
    }

    private sealed class DefaultCodeValidator : AbstractValidator<TestRequest>
    {
        public DefaultCodeValidator()
        {
            RuleFor(x => x.Name).MaximumLength(2);
        }
    }

    private sealed record TestGenericRequest : IRequest<Result<Guid>>;

    private sealed class FailingGenericValidator : AbstractValidator<TestGenericRequest>
    {
        public FailingGenericValidator()
        {
            RuleFor(x => x).Custom((_, context) => context.AddFailure(new ValidationFailure("Id", "Invalid.")));
        }
    }

    [Fact]
    public async Task Handle_NoValidatorsRegistered_InvokesNext()
    {
        var behavior = new ValidationBehavior<TestRequest, Result>([]);
        var nextCalled = false;

        var result = await behavior.Handle(new TestRequest("x"), () =>
        {
            nextCalled = true;
            return Task.FromResult(Result.Success());
        }, CancellationToken.None);

        nextCalled.Should().BeTrue();
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_OneFailingValidator_ReturnsFailureWithoutCallingNext()
    {
        var behavior = new ValidationBehavior<TestRequest, Result>([new FailingValidator("Name", "Name is required.")]);
        var nextCalled = false;

        var result = await behavior.Handle(new TestRequest(""), () =>
        {
            nextCalled = true;
            return Task.FromResult(Result.Success());
        }, CancellationToken.None);

        nextCalled.Should().BeFalse();
        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
        result.Error.Details.Should().ContainSingle(e => e.Code == ErrorCodes.Validation.Failed && e.Message == "Name is required.");
    }

    [Fact]
    public async Task Handle_MultipleValidatorsMixedPassFail_AggregatesOnlyFailingValidatorsErrors()
    {
        var behavior = new ValidationBehavior<TestRequest, Result>(
        [
            new PassingValidator(),
            new FailingValidator("Name", "Name is required."),
            new FailingValidator("Age", "Age must be positive."),
        ]);

        var result = await behavior.Handle(new TestRequest(""), () => Task.FromResult(Result.Success()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Details.Should().HaveCount(2);
        result.Error.Details.Select(e => e.MessageArguments[ErrorArgumentNames.PropertyPath]).Should().BeEquivalentTo(["Name", "Age"]);
    }

    [Fact]
    public async Task Handle_MultipleValidators_RunSequentiallyNotConcurrently()
    {
        var order = new List<int>();
        var behavior = new ValidationBehavior<TestRequest, Result>(
        [
            new SequentialOrderRecordingValidator(order, 1),
            new SequentialOrderRecordingValidator(order, 2),
            new SequentialOrderRecordingValidator(order, 3),
        ]);

        await behavior.Handle(new TestRequest("x"), () => Task.FromResult(Result.Success()), CancellationToken.None);

        order.Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task Handle_FailureWithErrorCode_UsesErrorCodeAndRecordsFieldPath()
    {
        var behavior = new ValidationBehavior<TransferRequest, Result>([new TransferValidator()]);

        var result = await behavior.Handle(
            new TransferRequest([new Account("TR000000000000000000000000")]),
            () => Task.FromResult(Result.Success()),
            CancellationToken.None);

        var error = result.Error.Details.Should().ContainSingle().Subject;
        error.Code.Should().Be("validation.iban.invalid_check_digits");
        error.Message.Should().Be("IBAN check digits are not correct.");
        error.MessageArguments[ErrorArgumentNames.PropertyPath].Should().Be("Accounts[0].Iban");
        error.MessageArguments[ErrorArgumentNames.PropertyName].Should().Be("IBAN");
    }

    [Fact]
    public async Task Handle_Failure_NeverCarriesTheAttemptedValue()
    {
        const string attempted = "TR000000000000000000000000";
        var behavior = new ValidationBehavior<TransferRequest, Result>([new TransferValidator()]);

        var result = await behavior.Handle(
            new TransferRequest([new Account(attempted)]),
            () => Task.FromResult(Result.Success()),
            CancellationToken.None);

        var error = result.Error.Details.Should().ContainSingle().Subject;
        error.MessageArguments.Should().NotContainKey("PropertyValue");
        error.MessageArguments.Values.Should().NotContain(attempted);
    }

    [Fact]
    public async Task Handle_BuiltInValidatorWithoutCustomCode_UsesFluentValidationDefaultCodeAndPlaceholders()
    {
        var behavior = new ValidationBehavior<TestRequest, Result>([new DefaultCodeValidator()]);

        var result = await behavior.Handle(new TestRequest("abcdef"), () => Task.FromResult(Result.Success()), CancellationToken.None);

        var error = result.Error.Details.Should().ContainSingle().Subject;
        error.Code.Should().Be("MaximumLengthValidator");
        error.MessageArguments.Should().ContainKey("MaxLength");
        error.MessageArguments[ErrorArgumentNames.PropertyPath].Should().Be("Name");
        error.MessageArguments.Should().NotContainKey("PropertyValue");
    }

    [Fact]
    public async Task Handle_HandBuiltFailureWithoutCode_FallsBackToGenericValidationCode()
    {
        var behavior = new ValidationBehavior<TestRequest, Result>([new FailingValidator("Name", "Name is required.")]);

        var result = await behavior.Handle(new TestRequest(""), () => Task.FromResult(Result.Success()), CancellationToken.None);

        var error = result.Error.Details.Should().ContainSingle().Subject;
        error.Code.Should().Be(ErrorCodes.Validation.Failed);
        error.MessageArguments[ErrorArgumentNames.PropertyPath].Should().Be("Name");
        error.MessageArguments[ErrorArgumentNames.PropertyName].Should().Be("Name");
    }

    [Fact]
    public async Task Handle_GenericResultResponse_ReturnsTypedFailure()
    {
        var behavior = new ValidationBehavior<TestGenericRequest, Result<Guid>>([new FailingGenericValidator()]);

        var result = await behavior.Handle(new TestGenericRequest(), () => Task.FromResult(Result<Guid>.Success(Guid.NewGuid())), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
    }
}
