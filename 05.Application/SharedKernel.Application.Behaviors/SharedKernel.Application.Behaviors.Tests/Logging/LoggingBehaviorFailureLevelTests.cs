using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SharedKernel.Application.Behaviors.Logging;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application.Behaviors.Tests.Logging;

// Top-level request types so NSubstitute can proxy ILogger<T> over them.

/// <summary>Request whose handler returns <see cref="Result.Success()"/>.</summary>
public sealed record SuccessResultCommand : IRequest<Result>;

/// <summary>Request whose handler returns <see cref="Result.Failure(SharedKernel.Primitives.Errors.Error)"/>.</summary>
public sealed record FailureResultCommand : IRequest<Result>;

/// <summary>A request that returns a raw <see cref="string"/> (non-<c>IHasSuccessFlag</c> response type).</summary>
public sealed record RawStringCommand : IRequest<string>;

/// <summary>Request whose handler throws.</summary>
public sealed record ThrowingResultCommand : IRequest<Result>;

/// <summary>
/// Verifies <see cref="LoggingBehavior{TRequest,TResponse}"/>'s failure-level protocol (T-22):
/// <list type="bullet">
///   <item><description>Successful handler → post-handler log at <c>Information</c>.</description></item>
///   <item><description>Handler returns <c>Result.Failure</c> → post-handler log at <c>Warning</c>.</description></item>
///   <item><description>Non-<c>IHasSuccessFlag</c> response type → post-handler log at <c>Information</c>.</description></item>
///   <item><description>Exception → <c>Error</c> + rethrow unchanged.</description></item>
/// </list>
/// </summary>
public sealed class LoggingBehaviorFailureLevelTests
{
    private sealed class SuccessHandler : IRequestHandler<SuccessResultCommand, Result>
    {
        public Task<Result> Handle(SuccessResultCommand request, CancellationToken ct)
            => Task.FromResult(Result.Success());
    }

    private sealed class FailureHandler : IRequestHandler<FailureResultCommand, Result>
    {
        public Task<Result> Handle(FailureResultCommand request, CancellationToken ct)
            => Task.FromResult(Result.Failure(SharedKernel.Primitives.Errors.Error.Conflict("x", "failed")));
    }

    private sealed class RawStringHandler : IRequestHandler<RawStringCommand, string>
    {
        public Task<string> Handle(RawStringCommand request, CancellationToken ct)
            => Task.FromResult("hello");
    }

    private sealed class ThrowingHandler : IRequestHandler<ThrowingResultCommand, Result>
    {
        public Task<Result> Handle(ThrowingResultCommand request, CancellationToken ct)
            => throw new InvalidOperationException("boom");
    }

    [Fact]
    public async Task Handle_ResultSuccess_PostHandlerLogIsAtInformation()
    {
        var logger = Substitute.For<ILogger<SuccessResultCommand>>();
        var services = new ServiceCollection();
        services.AddSingleton(logger);
        services.AddTransient<IRequestHandler<SuccessResultCommand, Result>, SuccessHandler>();
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<LoggingBehaviorFailureLevelTests>());
        var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<ISender>().Send(new SuccessResultCommand());

        // Two Information logs: start + success completion.
        logger.Received(2).Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            null,
            Arg.Any<Func<object, Exception?, string>>());
        logger.DidNotReceive().Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task Handle_ResultFailure_PostHandlerLogIsAtWarning()
    {
        var logger = Substitute.For<ILogger<FailureResultCommand>>();
        var services = new ServiceCollection();
        services.AddSingleton(logger);
        services.AddTransient<IRequestHandler<FailureResultCommand, Result>, FailureHandler>();
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<LoggingBehaviorFailureLevelTests>());
        var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<ISender>().Send(new FailureResultCommand());

        // One Information log (start) + one Warning (failure completion).
        logger.Received(1).Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            null,
            Arg.Any<Func<object, Exception?, string>>());
        logger.Received(1).Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            null,
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task Handle_NonIHasSuccessFlagResponseType_PostHandlerLogIsAtInformation()
    {
        var logger = Substitute.For<ILogger<RawStringCommand>>();
        var services = new ServiceCollection();
        services.AddSingleton(logger);
        services.AddTransient<IRequestHandler<RawStringCommand, string>, RawStringHandler>();
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<LoggingBehaviorFailureLevelTests>());
        var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<ISender>().Send(new RawStringCommand());

        // Two Information logs (start + completion); no Warning.
        logger.Received(2).Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            null,
            Arg.Any<Func<object, Exception?, string>>());
        logger.DidNotReceive().Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task Handle_HandlerThrows_LogsErrorAndRethrowsUnchanged()
    {
        var logger = Substitute.For<ILogger<ThrowingResultCommand>>();
        var services = new ServiceCollection();
        services.AddSingleton(logger);
        services.AddTransient<IRequestHandler<ThrowingResultCommand, Result>, ThrowingHandler>();
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<LoggingBehaviorFailureLevelTests>());
        var provider = services.BuildServiceProvider();

        var act = async () => await provider.GetRequiredService<ISender>().Send(new ThrowingResultCommand());

        var thrown = await act.Should().ThrowAsync<InvalidOperationException>();
        thrown.Which.Message.Should().Be("boom");

        logger.Received(1).Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            null,
            Arg.Any<Func<object, Exception?, string>>());
        logger.Received(1).Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Is<Exception>(ex => ex is InvalidOperationException && ex.Message == "boom"),
            Arg.Any<Func<object, Exception?, string>>());
    }
}
