using FluentAssertions;
using SharedKernel.Application.Messaging;
using NSubstitute;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Scheduling.Jobs;
using SharedKernel.Scheduling.Tests.TestSupport;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Logging;
using Xunit;

namespace SharedKernel.Scheduling.Tests.Jobs;

/// <summary>
/// T-05 — <see cref="ScheduledCommandJob{TCommand}"/> dispatches through <see cref="ISender"/> using
/// the command produced by the registered factory, and surfaces a failed <see cref="Result"/> rather
/// than swallowing it (mirrors <c>17.Workflows</c>' <c>CommandActivitySwallowFailureTests</c> — the
/// happy-path-only test that would miss this domain's most damaging bug shape).
/// </summary>
public sealed class ScheduledCommandJobTests
{
    private static ScheduledJobExecutionContext NewContext(string jobName = "job-1") => new()
    {
        JobName = jobName,
        ScheduledFireTimeUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        ActualFireTimeUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
    };

    [Fact]
    public async Task ExecuteAsync_SenderReturnsSuccess_ReturnsSuccessResult()
    {
        ISender sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<RecordingCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success()));

        var job = new ScheduledCommandJob<RecordingCommand>(sender, new FakeClock(), new InMemoryLogger<ScheduledCommandJob<RecordingCommand>>());

        Result result = await job.ExecuteAsync(_ => new RecordingCommand(), NewContext(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_SenderReturnsFailure_SurfacesFailureResult_DoesNotSwallow()
    {
        ISender sender = Substitute.For<ISender>();
        Error error = Error.Conflict("scheduling.test.conflict", "duplicate reconciliation run");
        sender.Send(Arg.Any<RecordingCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Failure(error)));

        var job = new ScheduledCommandJob<RecordingCommand>(sender, new FakeClock(), new InMemoryLogger<ScheduledCommandJob<RecordingCommand>>());

        Result result = await job.ExecuteAsync(_ => new RecordingCommand(), NewContext(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
    }

    [Fact]
    public async Task ExecuteAsync_InvokesCommandFactoryWithSuppliedContext()
    {
        ISender sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<RecordingCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success()));

        var job = new ScheduledCommandJob<RecordingCommand>(sender, new FakeClock(), new InMemoryLogger<ScheduledCommandJob<RecordingCommand>>());
        ScheduledJobExecutionContext context = NewContext("my-job");
        ScheduledJobExecutionContext? observed = null;

        await job.ExecuteAsync(
            ctx =>
            {
                observed = ctx;
                return new RecordingCommand();
            },
            context,
            CancellationToken.None);

        observed.Should().BeSameAs(context);
    }

    [Fact]
    public async Task ExecuteAsync_DispatchesThroughSenderExactlyOnce()
    {
        ISender sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<RecordingCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success()));

        var job = new ScheduledCommandJob<RecordingCommand>(sender, new FakeClock(), new InMemoryLogger<ScheduledCommandJob<RecordingCommand>>());

        await job.ExecuteAsync(_ => new RecordingCommand(), NewContext(), CancellationToken.None);

        await sender.Received(1).Send(Arg.Any<RecordingCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_PropagatesCancellationTokenToSender()
    {
        ISender sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<RecordingCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success()));

        var job = new ScheduledCommandJob<RecordingCommand>(sender, new FakeClock(), new InMemoryLogger<ScheduledCommandJob<RecordingCommand>>());
        using var cts = new CancellationTokenSource();

        await job.ExecuteAsync(_ => new RecordingCommand(), NewContext(), cts.Token);

        await sender.Received(1).Send(Arg.Any<RecordingCommand>(), cts.Token);
    }
}
