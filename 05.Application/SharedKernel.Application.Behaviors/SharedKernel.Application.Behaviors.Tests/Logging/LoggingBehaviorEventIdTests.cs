using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SharedKernel.Application.Behaviors.Logging;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application.Behaviors.Tests.Logging;

/// <summary>Top-level request type used only to assert the entry-log <c>EventId</c>.</summary>
public sealed record EventIdSuccessCommand : IRequest<Result>;

/// <summary>Top-level request type used only to assert the fault-log <c>EventId</c>.</summary>
public sealed record EventIdThrowingCommand : IRequest<Result>;

/// <summary>Top-level request type used only to assert the failure-completion-log <c>EventId</c>.</summary>
public sealed record EventIdFailureCommand : IRequest<Result>;

/// <summary>
/// Verifies (WO-041, T-61) that <see cref="LoggingBehavior{TRequest,TResponse}"/>'s four
/// <c>[LoggerMessage]</c>-attributed log calls each carry the correct, registry-derived
/// <c>EventId</c> (5100/5101/5102/5103) after the P-253 authoring-mechanism retrofit.
/// </summary>
public sealed class LoggingBehaviorEventIdTests
{
    private sealed class SucceedingHandler : IRequestHandler<EventIdSuccessCommand, Result>
    {
        public Task<Result> Handle(EventIdSuccessCommand request, CancellationToken ct)
            => Task.FromResult(Result.Success());
    }

    private sealed class FailingHandler : IRequestHandler<EventIdFailureCommand, Result>
    {
        public Task<Result> Handle(EventIdFailureCommand request, CancellationToken ct)
            => Task.FromResult(Result.Failure(SharedKernel.Primitives.Errors.Error.Conflict("x", "failed")));
    }

    private sealed class ThrowingHandler : IRequestHandler<EventIdThrowingCommand, Result>
    {
        public Task<Result> Handle(EventIdThrowingCommand request, CancellationToken ct)
            => throw new InvalidOperationException("boom");
    }

    [Fact]
    public async Task Handle_OnSuccess_LogsEntryAndCompletionWithEventIds5100And5101()
    {
        var logger = Substitute.For<ILogger<EventIdSuccessCommand>>();
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);

        var services = new ServiceCollection();
        services.AddSingleton(logger);
        services.AddTransient<IRequestHandler<EventIdSuccessCommand, Result>, SucceedingHandler>();
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<LoggingBehaviorEventIdTests>());
        var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<ISender>().Send(new EventIdSuccessCommand());

        logger.Received(1).Log(
            LogLevel.Information,
            Arg.Is<EventId>(e => e.Id == 5100),
            Arg.Any<object>(),
            null,
            Arg.Any<Func<object, Exception?, string>>());

        logger.Received(1).Log(
            LogLevel.Information,
            Arg.Is<EventId>(e => e.Id == 5101),
            Arg.Any<object>(),
            null,
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task Handle_OnFailureResult_LogsCompletionWithEventId5102()
    {
        var logger = Substitute.For<ILogger<EventIdFailureCommand>>();
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);

        var services = new ServiceCollection();
        services.AddSingleton(logger);
        services.AddTransient<IRequestHandler<EventIdFailureCommand, Result>, FailingHandler>();
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<LoggingBehaviorEventIdTests>());
        var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<ISender>().Send(new EventIdFailureCommand());

        logger.Received(1).Log(
            LogLevel.Warning,
            Arg.Is<EventId>(e => e.Id == 5102),
            Arg.Any<object>(),
            null,
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task Handle_OnException_LogsFaultWithEventId5103()
    {
        var logger = Substitute.For<ILogger<EventIdThrowingCommand>>();
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);

        var services = new ServiceCollection();
        services.AddSingleton(logger);
        services.AddTransient<IRequestHandler<EventIdThrowingCommand, Result>, ThrowingHandler>();
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<LoggingBehaviorEventIdTests>());
        var provider = services.BuildServiceProvider();

        var act = async () => await provider.GetRequiredService<ISender>().Send(new EventIdThrowingCommand());
        await act.Should().ThrowAsync<InvalidOperationException>();

        logger.Received(1).Log(
            LogLevel.Error,
            Arg.Is<EventId>(e => e.Id == 5103),
            Arg.Any<object>(),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }
}
