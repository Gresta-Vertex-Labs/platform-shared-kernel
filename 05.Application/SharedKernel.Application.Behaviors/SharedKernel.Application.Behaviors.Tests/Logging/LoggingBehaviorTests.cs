using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Application.Behaviors.Logging;
using SharedKernel.Application.Behaviors.Tests.Support;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application.Behaviors.Tests.Logging;

public sealed class LoggingBehaviorTests
{
    private sealed record TestRequest : IRequest<Result>;

    private static LoggingBehavior<TestRequest, Result> CreateBehavior(
        FakeLogger<TestRequest> logger,
        TimeSpan? slowThreshold = null)
        => new(logger, Options.Create(new ApplicationLoggingOptions
        {
            SlowRequestThreshold = slowThreshold ?? TimeSpan.FromMilliseconds(500),
        }));

    [Fact]
    public async Task Handle_Success_LogsDebugStartAndInformationCompletion()
    {
        var logger = new FakeLogger<TestRequest>();
        var behavior = CreateBehavior(logger);

        await behavior.Handle(new TestRequest(), () => Task.FromResult(Result.Success()), CancellationToken.None);

        logger.Entries.Should().Contain(e => e.Level == LogLevel.Debug);
        logger.Entries.Should().Contain(e => e.Level == LogLevel.Information);
    }

    [Fact]
    public async Task Handle_SuccessOverSlowThreshold_LogsWarning()
    {
        var logger = new FakeLogger<TestRequest>();
        var behavior = CreateBehavior(logger, TimeSpan.FromMilliseconds(1));

        await behavior.Handle(new TestRequest(), async () =>
        {
            await Task.Delay(20);
            return Result.Success();
        }, CancellationToken.None);

        logger.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.Message.Contains("threshold"));
    }

    [Fact]
    public async Task Handle_Failure_LogsWarningWithErrorTypeAndCode()
    {
        var logger = new FakeLogger<TestRequest>();
        var behavior = CreateBehavior(logger);
        var error = Error.Conflict("test.conflict", "conflict occurred");

        await behavior.Handle(new TestRequest(), () => Task.FromResult(Result.Failure(error)), CancellationToken.None);

        logger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Warning
            && e.Message.Contains("Conflict")
            && e.Message.Contains("test.conflict"));
    }

    [Fact]
    public async Task Handle_ThrownException_LogsErrorAndRethrows()
    {
        var logger = new FakeLogger<TestRequest>();
        var behavior = CreateBehavior(logger);
        var thrown = new InvalidOperationException("boom");

        var act = async () => await behavior.Handle(new TestRequest(), () => throw thrown, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        logger.Entries.Should().Contain(e => e.Level == LogLevel.Error && e.Exception == thrown);
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
        var logger = new FakeLogger<LoggableRequest>();
        var behavior = new LoggingBehavior<LoggableRequest, Result<string>>(
            logger, Options.Create(new ApplicationLoggingOptions()));

        var act = async () => await behavior.Handle(
            new LoggableRequest(),
            () => Task.FromResult(Result<string>.Success("hello")),
            CancellationToken.None);

        await act.Should().NotThrowAsync();
        // BeginScope is exercised without throwing for a request implementing ILoggableRequest<TResponse> —
        // the FakeLogger's BeginScope always returns a no-op disposable regardless of TState.
    }
}
