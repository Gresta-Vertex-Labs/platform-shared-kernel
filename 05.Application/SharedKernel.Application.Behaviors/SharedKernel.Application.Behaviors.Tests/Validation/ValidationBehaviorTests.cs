using FluentAssertions;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Application.Behaviors.Validation;
using SharedKernel.Primitives.Results;
using ValidationException = SharedKernel.Core.Exceptions.ValidationException;

namespace SharedKernel.Application.Behaviors.Tests.Validation;

/// <summary>
/// Verifies <see cref="ValidationBehavior{TRequest,TResponse}"/> using a real, minimal MediatR
/// pipeline rather than a hand-rolled <see cref="RequestHandlerDelegate{TResponse}"/> mock.
/// </summary>
public sealed class ValidationBehaviorTests
{
    private sealed record TestCommand(string Name) : IRequest<Result>;

    private sealed class TestCommandHandler : IRequestHandler<TestCommand, Result>
    {
        public bool WasInvoked { get; private set; }

        public Task<Result> Handle(TestCommand request, CancellationToken cancellationToken)
        {
            WasInvoked = true;
            return Task.FromResult(Result.Success());
        }
    }

    private sealed class AlwaysPassingValidator : AbstractValidator<TestCommand>
    {
        public AlwaysPassingValidator()
        {
            RuleFor(x => x.Name).NotEmpty();
        }
    }

    private sealed class AlwaysFailingValidator : AbstractValidator<TestCommand>
    {
        public AlwaysFailingValidator()
        {
            RuleFor(x => x.Name).Must(_ => false).WithMessage("always fails");
        }
    }

    private sealed class SecondFailingValidator : AbstractValidator<TestCommand>
    {
        public SecondFailingValidator()
        {
            RuleFor(x => x.Name).Must(_ => false).WithMessage("second failure");
        }
    }

    private static ServiceProvider BuildProvider(TestCommandHandler handler, params IValidator<TestCommand>[] validators)
    {
        var services = new ServiceCollection();
        services.AddSingleton(handler);
        services.AddSingleton<IRequestHandler<TestCommand, Result>>(sp => sp.GetRequiredService<TestCommandHandler>());

        foreach (var validator in validators)
            services.AddSingleton(validator);

        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<ValidationBehaviorTests>());

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Handle_WithZeroRegisteredValidators_InvokesHandler()
    {
        var handler = new TestCommandHandler();
        var provider = BuildProvider(handler);
        var sender = provider.GetRequiredService<ISender>();

        var result = await sender.Send(new TestCommand("anything"));

        handler.WasInvoked.Should().BeTrue();
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WithOneFailingValidator_ThrowsValidationExceptionAndNeverInvokesHandler()
    {
        var handler = new TestCommandHandler();
        var provider = BuildProvider(handler, new AlwaysFailingValidator());
        var sender = provider.GetRequiredService<ISender>();

        var act = async () => await sender.Send(new TestCommand("anything"));

        var thrown = await act.Should().ThrowAsync<ValidationException>();
        thrown.Which.Errors.Should().ContainSingle(e => e.Message == "always fails");
        handler.WasInvoked.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_WithMixedPassingAndFailingValidators_AggregatesOnlyFailingErrors()
    {
        var handler = new TestCommandHandler();
        var provider = BuildProvider(
            handler,
            new AlwaysPassingValidator(),
            new AlwaysFailingValidator(),
            new SecondFailingValidator());
        var sender = provider.GetRequiredService<ISender>();

        var act = async () => await sender.Send(new TestCommand("anything"));

        var thrown = await act.Should().ThrowAsync<ValidationException>();
        thrown.Which.Errors.Should().HaveCount(2);
        thrown.Which.Errors.Should().Contain(e => e.Message == "always fails");
        thrown.Which.Errors.Should().Contain(e => e.Message == "second failure");
        handler.WasInvoked.Should().BeFalse();
    }
}
