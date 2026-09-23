using FluentAssertions;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedKernel.Presentation.Grpc.Interceptors;
using SharedKernel.Presentation.Grpc.Tests.Integration.Fixtures;
using SharedKernel.Primitives.Logging;
using SharedKernel.Testing.Logging;
using Xunit;

namespace SharedKernel.Presentation.Grpc.Tests.Integration;

/// <summary>
/// Design D13: a call the client cancels ends as <see cref="StatusCode.Cancelled"/>; the service's
/// <see cref="OperationCanceledException"/> is logged at Debug, never as an error.
/// </summary>
public sealed class CancellationTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task ClientCancellation_EndsAsCancelled_WithoutAnErrorLog()
    {
        var logs = new InMemoryLoggerFactory();
        await using var app = await GrpcTestHost.StartAsync(loggerFactory: logs);
        var probe = app.Services.GetRequiredService<CallProbe>();
        using var cancellation = new CancellationTokenSource();

        using var call = app.CreateClient().WaitForCancellationAsync(new EchoRequest(), cancellationToken: cancellation.Token);
        await probe.Entered.Task.WaitAsync(Patience);
        await cancellation.CancelAsync();
        var act = async () => await call;

        (await act.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.Cancelled);

        // The client stops waiting at once; the server notices the abort a moment later.
        var interceptorLog = logs.GetLogger(typeof(GrpcExceptionInterceptor).FullName!);
        await WaitUntilAsync(() => interceptorLog.Records.Any(record => record.EventId.Id == LoggingEventIdRanges.Presentation + 204));

        var cancelled = interceptorLog.Records.Should().ContainSingle().Subject;
        cancelled.LogLevel.Should().Be(LogLevel.Debug);
        cancelled.Exception.Should().BeAssignableTo<OperationCanceledException>();
        logs.Loggers.Values.SelectMany(logger => logger.Records)
            .Should().NotContain(record => record.LogLevel >= LogLevel.Error, "a cancelled call is not a failure of the service");
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (!condition())
        {
            DateTime.UtcNow.Should().BeBefore(deadline, "the server should have logged the cancellation");
            await Task.Delay(20);
        }
    }
}
