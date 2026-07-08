using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SharedKernel.Application.Behaviors.Logging;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application.Behaviors.Tests.Logging;

/// <summary>
/// A request that does NOT opt in to <see cref="ILoggableRequest{TResponse}"/> — the WO-040 regression
/// baseline (T-57): its log call sequence must be byte-for-byte identical to before the capability
/// existed, and it must never trigger an <see cref="ILogger.BeginScope{TState}"/> call.
/// </summary>
public sealed record NonLoggableTestCommand : IRequest<Result>;

/// <summary>
/// Opt-in request (WO-040) whose request-side and response-side loggable field sets, and whether the
/// response-field factory was invoked, are all configurable per test.
/// </summary>
public sealed class LoggableTestCommand : ILoggableRequest<Result>
{
    public required IReadOnlyDictionary<string, object?> LoggableRequestFields { get; init; }

    public required Func<Result, IReadOnlyDictionary<string, object?>?> ResponseFieldsFactory { get; init; }

    public bool ResponseFieldsFactoryInvoked { get; private set; }

    public IReadOnlyDictionary<string, object?>? GetLoggableResponseFields(Result response)
    {
        ResponseFieldsFactoryInvoked = true;
        return ResponseFieldsFactory(response);
    }
}

/// <summary>
/// Verifies the WO-040 <see cref="ILoggableRequest{TResponse}"/> opt-in structured request/response
/// payload logging capability shipped in <see cref="LoggingBehavior{TRequest,TResponse}"/> (T-53..T-58).
/// </summary>
public sealed class LoggingBehaviorLoggableRequestTests
{
    private sealed class NonLoggableHandler : IRequestHandler<NonLoggableTestCommand, Result>
    {
        public Task<Result> Handle(NonLoggableTestCommand request, CancellationToken ct)
            => Task.FromResult(Result.Success());
    }

    private sealed class SucceedingLoggableHandler : IRequestHandler<LoggableTestCommand, Result>
    {
        public Task<Result> Handle(LoggableTestCommand request, CancellationToken ct)
            => Task.FromResult(Result.Success());
    }

    private sealed class FailingLoggableHandler : IRequestHandler<LoggableTestCommand, Result>
    {
        public Task<Result> Handle(LoggableTestCommand request, CancellationToken ct)
            => Task.FromResult(Result.Failure(Error.Conflict("x", "failed")));
    }

    private sealed class ThrowingLoggableHandler : IRequestHandler<LoggableTestCommand, Result>
    {
        public Task<Result> Handle(LoggableTestCommand request, CancellationToken ct)
            => throw new InvalidOperationException("loggable handler blew up");
    }

    private static ServiceProvider BuildProvider<TRequest, TResponse, THandler>(ILogger<TRequest> logger)
        where TRequest : IRequest<TResponse>
        where THandler : class, IRequestHandler<TRequest, TResponse>
    {
        var services = new ServiceCollection();
        services.AddSingleton(logger);
        services.AddTransient<IRequestHandler<TRequest, TResponse>, THandler>();
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<LoggingBehaviorLoggableRequestTests>());

        return services.BuildServiceProvider();
    }

    // T-53: an ILoggableRequest<TResponse> attaches LoggableRequestFields to the entry-log scope.
    [Fact]
    public async Task Handle_LoggableRequest_AttachesRequestFieldsToEntryLogScope()
    {
        var logger = Substitute.For<ILogger<LoggableTestCommand>>();
        var provider = BuildProvider<LoggableTestCommand, Result, SucceedingLoggableHandler>(logger);
        var sender = provider.GetRequiredService<ISender>();

        var request = new LoggableTestCommand
        {
            LoggableRequestFields = new Dictionary<string, object?> { ["OrderId"] = "order-42" },
            // Opt out of response-side logging so only the entry-log scope is exercised here.
            ResponseFieldsFactory = _ => null,
        };

        await sender.Send(request);

        logger.Received(1).BeginScope(
            Arg.Is<IReadOnlyDictionary<string, object?>>(
                d => d.Count == 1 && Equals(d["OrderId"], "order-42")));
    }

    // T-54: GetLoggableResponseFields(response) is attached to the completion-log scope on Result.Success.
    [Fact]
    public async Task Handle_SuccessResponse_AttachesResponseFieldsToCompletionLogScope()
    {
        var logger = Substitute.For<ILogger<LoggableTestCommand>>();
        var provider = BuildProvider<LoggableTestCommand, Result, SucceedingLoggableHandler>(logger);
        var sender = provider.GetRequiredService<ISender>();

        var request = new LoggableTestCommand
        {
            // Opt out of request-side logging so only the completion-log scope is exercised here.
            LoggableRequestFields = new Dictionary<string, object?>(),
            ResponseFieldsFactory = result => new Dictionary<string, object?>
            {
                ["Outcome"] = result.IsSuccess ? "success" : "failure",
            },
        };

        await sender.Send(request);

        request.ResponseFieldsFactoryInvoked.Should().BeTrue();
        logger.Received(1).BeginScope(
            Arg.Is<IReadOnlyDictionary<string, object?>>(
                d => d.Count == 1 && Equals(d["Outcome"], "success")));
    }

    // T-55: response fields still attached to the completion-log scope on Result.Failure
    // (Warning-level entry per the existing IHasSuccessFlag rule) — fields and failure-level logging
    // compose correctly.
    [Fact]
    public async Task Handle_FailureResponse_AttachesResponseFieldsToCompletionLogScopeAndLogsWarning()
    {
        var logger = Substitute.For<ILogger<LoggableTestCommand>>();
        var provider = BuildProvider<LoggableTestCommand, Result, FailingLoggableHandler>(logger);
        var sender = provider.GetRequiredService<ISender>();

        var request = new LoggableTestCommand
        {
            LoggableRequestFields = new Dictionary<string, object?>(),
            ResponseFieldsFactory = result => new Dictionary<string, object?>
            {
                ["Outcome"] = result.IsSuccess ? "success" : "failure",
            },
        };

        await sender.Send(request);

        request.ResponseFieldsFactoryInvoked.Should().BeTrue();
        logger.Received(1).BeginScope(
            Arg.Is<IReadOnlyDictionary<string, object?>>(
                d => d.Count == 1 && Equals(d["Outcome"], "failure")));
        logger.Received(1).Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            null,
            Arg.Any<Func<object, Exception?, string>>());
    }

    // T-56: GetLoggableResponseFields is NEVER invoked and no response-fields scope is entered when
    // next() throws — only the request-fields scope (if opted in) applies to the entry log and the
    // existing Error-level exception log.
    [Fact]
    public async Task Handle_HandlerThrows_NeverInvokesResponseFieldsFactoryOrEntersResponseScope()
    {
        var logger = Substitute.For<ILogger<LoggableTestCommand>>();
        var provider = BuildProvider<LoggableTestCommand, Result, ThrowingLoggableHandler>(logger);
        var sender = provider.GetRequiredService<ISender>();

        var responseFieldsFactoryCalled = false;
        var request = new LoggableTestCommand
        {
            LoggableRequestFields = new Dictionary<string, object?> { ["OrderId"] = "order-99" },
            ResponseFieldsFactory = _ =>
            {
                responseFieldsFactoryCalled = true;
                return new Dictionary<string, object?> { ["ShouldNeverAppear"] = true };
            },
        };

        var act = async () => await sender.Send(request);

        await act.Should().ThrowAsync<InvalidOperationException>();

        responseFieldsFactoryCalled.Should().BeFalse();
        request.ResponseFieldsFactoryInvoked.Should().BeFalse();

        // Request-fields scope wraps both the entry Information log and the fault Error log — two calls.
        logger.Received(2).BeginScope(
            Arg.Is<IReadOnlyDictionary<string, object?>>(
                d => d.Count == 1 && Equals(d["OrderId"], "order-99")));

        logger.Received(1).Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Is<Exception>(ex => ex is InvalidOperationException),
            Arg.Any<Func<object, Exception?, string>>());
    }

    // T-57: a request type NOT implementing ILoggableRequest<TResponse> produces a byte-for-byte
    // identical log call sequence to the pre-WO-040 baseline — no BeginScope call at all.
    [Fact]
    public async Task Handle_NonLoggableRequest_NeverEntersAnyLoggingScope()
    {
        var logger = Substitute.For<ILogger<NonLoggableTestCommand>>();
        var provider = BuildProvider<NonLoggableTestCommand, Result, NonLoggableHandler>(logger);
        var sender = provider.GetRequiredService<ISender>();

        await sender.Send(new NonLoggableTestCommand());

        logger.DidNotReceive().BeginScope(Arg.Any<IReadOnlyDictionary<string, object?>>());

        // Same two-Information-log baseline shape as the pre-WO-040 LoggingBehaviorTests suite.
        logger.Received(2).Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            null,
            Arg.Any<Func<object, Exception?, string>>());
    }

    // T-58: an opted-in request whose LoggableRequestFields/GetLoggableResponseFields both return an
    // empty dictionary enters no scope at all — the zero-overhead path.
    [Fact]
    public async Task Handle_EmptyFieldDictionaries_EntersNoLoggingScope()
    {
        var logger = Substitute.For<ILogger<LoggableTestCommand>>();
        var provider = BuildProvider<LoggableTestCommand, Result, SucceedingLoggableHandler>(logger);
        var sender = provider.GetRequiredService<ISender>();

        var request = new LoggableTestCommand
        {
            LoggableRequestFields = new Dictionary<string, object?>(),
            ResponseFieldsFactory = _ => new Dictionary<string, object?>(),
        };

        await sender.Send(request);

        request.ResponseFieldsFactoryInvoked.Should().BeTrue();
        logger.DidNotReceive().BeginScope(Arg.Any<IReadOnlyDictionary<string, object?>>());
    }
}
