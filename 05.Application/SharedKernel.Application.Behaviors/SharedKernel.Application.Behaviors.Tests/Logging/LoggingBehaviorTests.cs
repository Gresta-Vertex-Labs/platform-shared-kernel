using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SharedKernel.Application.Behaviors.Logging;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application.Behaviors.Tests.Logging;

/// <summary>
/// A top-level (non-nested) request type so NSubstitute's Castle proxy generator can build
/// <c>ILogger&lt;LoggingTestCommand&gt;</c> — proxying a generic interface closed over a private
/// nested type fails with an accessibility error.
/// </summary>
public sealed record LoggingTestCommand : IRequest<Result>;

/// <summary>
/// Verifies <see cref="LoggingBehavior{TRequest,TResponse}"/>'s success and fault logging paths
/// using a real, minimal MediatR pipeline.
/// </summary>
public sealed class LoggingBehaviorTests
{
    private sealed class ThrowingHandler : IRequestHandler<LoggingTestCommand, Result>
    {
        public Task<Result> Handle(LoggingTestCommand request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("handler blew up");
    }

    private sealed class SucceedingHandler : IRequestHandler<LoggingTestCommand, Result>
    {
        public Task<Result> Handle(LoggingTestCommand request, CancellationToken cancellationToken)
            => Task.FromResult(Result.Success());
    }

    private static ServiceProvider BuildProvider<THandler>(ILogger<LoggingTestCommand> logger)
        where THandler : class, IRequestHandler<LoggingTestCommand, Result>
    {
        var services = new ServiceCollection();
        services.AddSingleton(logger);
        services.AddTransient<IRequestHandler<LoggingTestCommand, Result>, THandler>();
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<LoggingBehaviorTests>());

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Handle_OnSuccess_LogsStartAndSuccessAtInformation()
    {
        var logger = Substitute.For<ILogger<LoggingTestCommand>>();
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        var provider = BuildProvider<SucceedingHandler>(logger);
        var sender = provider.GetRequiredService<ISender>();

        await sender.Send(new LoggingTestCommand());

        logger.Received(2).Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            null,
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task Handle_OnException_LogsStartAtInformationAndFaultAtErrorThenRethrowsUnchanged()
    {
        var logger = Substitute.For<ILogger<LoggingTestCommand>>();
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        var provider = BuildProvider<ThrowingHandler>(logger);
        var sender = provider.GetRequiredService<ISender>();

        var act = async () => await sender.Send(new LoggingTestCommand());

        var thrown = await act.Should().ThrowAsync<InvalidOperationException>();
        thrown.Which.Message.Should().Be("handler blew up");

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
            Arg.Is<Exception>(ex => ex is InvalidOperationException && ex.Message == "handler blew up"),
            Arg.Any<Func<object, Exception?, string>>());
    }
}
