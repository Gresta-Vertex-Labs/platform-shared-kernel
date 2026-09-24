using FluentAssertions;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedKernel.Core.Exceptions;
using SharedKernel.Presentation.Grpc.Interceptors;
using SharedKernel.Presentation.Grpc.Tests.Integration.Fixtures;
using SharedKernel.Primitives.Logging;
using SharedKernel.Testing.Logging;
using Xunit;

namespace SharedKernel.Presentation.Grpc.Tests.Integration;

/// <summary>
/// Design D13 and P-562 R33: a call the client cancels ends as <see cref="StatusCode.Cancelled"/>, and whatever the
/// service throws once its call is cancelled — the <see cref="OperationCanceledException"/> of a wait, the
/// <see cref="IOException"/> of an aborted stream, the <see cref="InvalidOperationException"/> of a write to a
/// completed call, a downstream call's <see cref="RpcException"/>, even a server error — is logged at Debug as a
/// cancellation, never as an error.
/// </summary>
public sealed class CancellationTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData(AfterCancellation.Wait, typeof(OperationCanceledException))]
    [InlineData(AfterCancellation.Io, typeof(IOException))]
    [InlineData(AfterCancellation.InvalidOperation, typeof(InvalidOperationException))]
    [InlineData(AfterCancellation.Rpc, typeof(RpcException))]
    [InlineData(AfterCancellation.ServerError, typeof(DomainException))]
    public async Task Unary_AnyExceptionOnceTheClientCancelled_EndsAsCancelled_WithoutAnErrorLog(string failure, Type thrown)
    {
        var logs = new InMemoryLoggerFactory();
        await using var app = await GrpcTestHost.StartAsync(loggerFactory: logs);
        var probe = app.Services.GetRequiredService<CallProbe>();
        using var cancellation = new CancellationTokenSource();

        using var call = app.CreateClient().WaitForCancellationAsync(new EchoRequest { Value = failure }, cancellationToken: cancellation.Token);
        await probe.Entered.Task.WaitAsync(Patience);
        await cancellation.CancelAsync();
        var act = async () => await call;

        (await act.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.Cancelled);
        await ShouldBeLoggedAsACancellationOnlyAsync(logs, thrown);
    }

    [Theory]
    [InlineData(AfterCancellation.Wait, typeof(OperationCanceledException))]
    [InlineData(AfterCancellation.Io, typeof(IOException))]
    [InlineData(AfterCancellation.InvalidOperation, typeof(InvalidOperationException))]
    public async Task ServerStreaming_AnyExceptionOnceTheClientCancelled_EndsAsCancelled_WithoutAnErrorLog(string failure, Type thrown)
    {
        var logs = new InMemoryLoggerFactory();
        await using var app = await GrpcTestHost.StartAsync(loggerFactory: logs);
        var probe = app.Services.GetRequiredService<CallProbe>();
        using var cancellation = new CancellationTokenSource();

        using var call = app.CreateClient().StreamUntilCancelled(new EchoRequest { Value = failure }, cancellationToken: cancellation.Token);
        (await call.ResponseStream.MoveNext(CancellationToken.None)).Should().BeTrue("the stream had started");
        await probe.Entered.Task.WaitAsync(Patience);
        await cancellation.CancelAsync();
        var act = async () => await call.ResponseStream.MoveNext(CancellationToken.None);

        (await act.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.Cancelled);
        await ShouldBeLoggedAsACancellationOnlyAsync(logs, thrown);
    }

    private static async Task ShouldBeLoggedAsACancellationOnlyAsync(InMemoryLoggerFactory logs, Type thrown)
    {
        // The client stops waiting at once; the server notices the abort a moment later.
        var interceptorLog = logs.GetLogger(typeof(GrpcExceptionInterceptor).FullName!);
        await WaitUntilAsync(() => interceptorLog.Records.Any(record => record.EventId.Id == LoggingEventIdRanges.Presentation + 204));

        var cancelled = interceptorLog.Records.Should().ContainSingle().Subject;
        cancelled.LogLevel.Should().Be(LogLevel.Debug);
        cancelled.Exception.Should().BeAssignableTo(thrown);
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
