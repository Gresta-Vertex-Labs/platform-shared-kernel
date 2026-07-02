using FluentAssertions;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Application.Behaviors.Streaming;
using SharedKernel.Application.Streaming;
using System.Runtime.CompilerServices;
using ValidationException = SharedKernel.Core.Exceptions.ValidationException;

namespace SharedKernel.Application.Behaviors.Tests.Streaming;

/// <summary>
/// Verifies <see cref="StreamValidationBehavior{TRequest,TResponse}"/> (T-30):
/// <list type="bullet">
///   <item>No validators registered → stream opens normally.</item>
///   <item>Failing validator → <see cref="ValidationException"/> thrown before stream opens.</item>
///   <item>Passing validator → stream opens normally.</item>
///   <item>Validation runs once per stream-open, not per item.</item>
/// </list>
/// </summary>
public sealed class StreamValidationBehaviorTests
{
    private sealed record ValidatableStreamQuery(string Name) : IStreamQuery<string>;

    private sealed class ValidatableStreamQueryHandler : IStreamRequestHandler<ValidatableStreamQuery, string>
    {
        public int OpenCount { get; private set; }

        public async IAsyncEnumerable<string> Handle(ValidatableStreamQuery request,
            [EnumeratorCancellation] CancellationToken ct)
        {
            OpenCount++;
            yield return request.Name;
            yield return request.Name + "_2";
            await Task.CompletedTask;
        }
    }

    private sealed class RejectAllValidator : AbstractValidator<ValidatableStreamQuery>
    {
        public RejectAllValidator()
        {
            RuleFor(x => x.Name).Must(_ => false).WithMessage("Name is always rejected.");
        }
    }

    private sealed class AcceptAllValidator : AbstractValidator<ValidatableStreamQuery>
    {
        public AcceptAllValidator()
        {
            RuleFor(x => x.Name).NotEmpty();
        }
    }

    [Fact]
    public async Task Handle_NoValidators_StreamOpensNormally()
    {
        var handler = new ValidatableStreamQueryHandler();
        var services = new ServiceCollection();
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IStreamRequestHandler<ValidatableStreamQuery, string>>(handler);
        // No IValidator<ValidatableStreamQuery> registered.
        services.AddTransient(typeof(IStreamPipelineBehavior<,>), typeof(StreamValidationBehavior<,>));
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<StreamValidationBehaviorTests>());
        var provider = services.BuildServiceProvider();

        var items = new List<string>();
        await foreach (var item in provider.GetRequiredService<ISender>().CreateStream(new ValidatableStreamQuery("test")))
            items.Add(item);

        items.Should().HaveCount(2);
        handler.OpenCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_FailingValidator_ThrowsValidationExceptionBeforeStreamOpens()
    {
        var handler = new ValidatableStreamQueryHandler();
        var services = new ServiceCollection();
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IStreamRequestHandler<ValidatableStreamQuery, string>>(handler);
        services.AddSingleton<IValidator<ValidatableStreamQuery>, RejectAllValidator>();
        services.AddTransient(typeof(IStreamPipelineBehavior<,>), typeof(StreamValidationBehavior<,>));
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<StreamValidationBehaviorTests>());
        var provider = services.BuildServiceProvider();

        var act = async () =>
        {
            await foreach (var _ in provider.GetRequiredService<ISender>().CreateStream(new ValidatableStreamQuery("x"))) { }
        };

        var thrown = await act.Should().ThrowAsync<ValidationException>();
        thrown.Which.Errors.Should().NotBeEmpty();

        // Handler's stream was never opened.
        handler.OpenCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_PassingValidator_StreamOpensAndYieldsItems()
    {
        var handler = new ValidatableStreamQueryHandler();
        var services = new ServiceCollection();
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IStreamRequestHandler<ValidatableStreamQuery, string>>(handler);
        services.AddSingleton<IValidator<ValidatableStreamQuery>, AcceptAllValidator>();
        services.AddTransient(typeof(IStreamPipelineBehavior<,>), typeof(StreamValidationBehavior<,>));
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<StreamValidationBehaviorTests>());
        var provider = services.BuildServiceProvider();

        var items = new List<string>();
        await foreach (var item in provider.GetRequiredService<ISender>().CreateStream(new ValidatableStreamQuery("valid")))
            items.Add(item);

        items.Should().HaveCount(2);
        handler.OpenCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_ValidationRunsOncePerStreamOpen_NotPerItem()
    {
        // Validator call count: with 2 items yielded, validation must still run only once.
        var validatorCallCount = 0;
        var services = new ServiceCollection();
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
        services.AddScoped<IStreamRequestHandler<ValidatableStreamQuery, string>, ValidatableStreamQueryHandler>();

        // Use a counting wrapper around AcceptAllValidator.
        var countingValidator = new InlineValidator<ValidatableStreamQuery>();
        countingValidator.RuleFor(x => x.Name)
            .Must(name =>
            {
                validatorCallCount++;
                return !string.IsNullOrEmpty(name);
            });
        services.AddSingleton<IValidator<ValidatableStreamQuery>>(countingValidator);

        services.AddTransient(typeof(IStreamPipelineBehavior<,>), typeof(StreamValidationBehavior<,>));
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<StreamValidationBehaviorTests>());
        var provider = services.BuildServiceProvider();

        var items = new List<string>();
        await foreach (var item in provider.GetRequiredService<ISender>().CreateStream(new ValidatableStreamQuery("once")))
            items.Add(item);

        items.Should().HaveCount(2);
        validatorCallCount.Should().Be(1, "validation must run once per stream-open, not once per item");
    }
}
