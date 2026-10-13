using SharedKernel.Application.Logging;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Application.Messaging;
using SharedKernel.Application.Pipeline.Logging;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Logging;
using SharedKernel.Primitives.Results;
using SharedKernel.Testing.Logging;

namespace SharedKernel.Application.Pipeline.Tests.Logging;

public sealed class LoggingBehaviorTests
{
    private const int Handling = 5100;
    private const int HandledSuccess = 5101;
    private const int HandledSlow = 5102;
    private const int HandledFailure = 5103;
    private const int HandlingThrew = 5104;
    private const int HandlingCanceled = 5105;

    private sealed record TestRequest : IRequest<Result>;

    private static (LoggingBehavior<TestRequest, Result> Behavior, InMemoryLogger<LoggingBehavior<TestRequest, Result>> Logger) CreateBehavior(
        TimeSpan? slowThreshold = null)
    {
        var logger = new InMemoryLogger<LoggingBehavior<TestRequest, Result>>();
        var behavior = new LoggingBehavior<TestRequest, Result>(
            logger,
            Options.Create(new ApplicationLoggingOptions
            {
                SlowRequestThreshold = slowThreshold ?? TimeSpan.FromMilliseconds(500),
            }));
        return (behavior, logger);
    }

    [Fact]
    public async Task Handle_Success_LogsDebugStartAndDebugCompletion()
    {
        var (behavior, logger) = CreateBehavior();

        await behavior.Handle(new TestRequest(), () => Task.FromResult(Result.Success()), CancellationToken.None);

        logger.Records.ShouldHaveLogged(Handling, LogLevel.Debug);
        logger.Records.ShouldHaveLogged(HandledSuccess, LogLevel.Debug);
        logger.Records.Should().NotContain(record => record.LogLevel >= LogLevel.Information);
        logger.Records.ShouldFollowLogConvention().ShouldOnlyLogEventIdsInSubBlock(LoggingEventIdRanges.Application, 1);
    }

    [Fact]
    public async Task Handle_SuccessOverSlowThreshold_LogsWarning()
    {
        var (behavior, logger) = CreateBehavior(TimeSpan.FromMilliseconds(1));

        await behavior.Handle(new TestRequest(), async () =>
        {
            await Task.Delay(20);
            return Result.Success();
        }, CancellationToken.None);

        logger.Records.ShouldHaveLogged(HandledSlow, LogLevel.Warning).Message.Should().Contain("threshold");
        logger.Records.ShouldNotHaveLogged(HandledSuccess);
    }

    [Fact]
    public async Task Handle_Failure_LogsWarningWithErrorTypeAndCode()
    {
        var (behavior, logger) = CreateBehavior();
        var error = Error.Conflict("test.conflict", "conflict occurred");

        await behavior.Handle(new TestRequest(), () => Task.FromResult(Result.Failure(error)), CancellationToken.None);

        var record = logger.Records.ShouldHaveLogged(HandledFailure, LogLevel.Warning);
        record.Message.Should().Contain("Conflict").And.Contain("test.conflict");
        record.Message.Should().NotContain("conflict occurred");
    }

    [Fact]
    public async Task Handle_ThrownException_LogsErrorWithTheExceptionAndRethrows()
    {
        var (behavior, logger) = CreateBehavior();
        var thrown = new InvalidOperationException("boom");

        var act = async () => await behavior.Handle(new TestRequest(), () => throw thrown, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        var record = logger.Records.ShouldHaveLogged(HandlingThrew, LogLevel.Error);
        record.Exception.Should().BeSameAs(thrown);
        record.Message.Should().NotContain("boom");
    }

    [Fact]
    public async Task Handle_Cancellation_LogsDebugNotErrorAndRethrows()
    {
        var (behavior, logger) = CreateBehavior();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () => await behavior.Handle(
            new TestRequest(),
            () => throw new OperationCanceledException(cts.Token),
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        logger.Records.ShouldHaveLogged(HandlingCanceled, LogLevel.Debug);
        logger.Records.ShouldNotHaveLogged(HandlingThrew);
    }

    [Fact]
    public async Task Handle_TheLoggerCategoryIsTheBehaviorNeverTheRequestType()
    {
        var factory = new InMemoryLoggerFactory();
        var behavior = new LoggingBehavior<TestRequest, Result>(
            factory.CreateLogger<LoggingBehavior<TestRequest, Result>>(),
            Options.Create(new ApplicationLoggingOptions()));

        await behavior.Handle(new TestRequest(), () => Task.FromResult(Result.Success()), CancellationToken.None);

        factory.ShouldHaveCategory(typeof(LoggingBehavior<TestRequest, Result>));
        factory.Loggers.Keys.Should().NotContain(key => key.EndsWith(nameof(TestRequest), StringComparison.Ordinal));
    }

    private sealed record LoggableRequest : IRequest<Result<string>>, ILoggableRequest<Result<string>>
    {
        public IReadOnlyDictionary<string, object?> LoggableRequestFields => new Dictionary<string, object?> { ["orderId"] = 42 };

        public IReadOnlyDictionary<string, object?>? GetLoggableResponseFields(Result<string> response)
            => response.IsSuccess ? new Dictionary<string, object?> { ["resultLength"] = response.Value.Length } : null;
    }

    [Fact]
    public async Task Handle_LoggableRequest_OpensRequestAndResponseScopes()
    {
        var logger = new InMemoryLogger<LoggingBehavior<LoggableRequest, Result<string>>>();
        var behavior = new LoggingBehavior<LoggableRequest, Result<string>>(
            logger, Options.Create(new ApplicationLoggingOptions()));

        var act = async () => await behavior.Handle(
            new LoggableRequest(),
            () => Task.FromResult(Result<string>.Success("hello")),
            CancellationToken.None);

        await act.Should().NotThrowAsync();
        logger.Records.ShouldHaveLogged(HandledSuccess, LogLevel.Debug);
    }
}
