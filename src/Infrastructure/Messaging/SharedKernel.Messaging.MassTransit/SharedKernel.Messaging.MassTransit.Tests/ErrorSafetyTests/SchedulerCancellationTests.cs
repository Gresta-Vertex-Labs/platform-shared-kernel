using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SharedKernel.Messaging.MassTransit.MessageBus;
using MtScheduler = MassTransit.IMessageScheduler;

namespace SharedKernel.Messaging.MassTransit.Tests.ErrorSafetyTests;

/// <summary>
/// SK.07.MessagingErrorSafety T-03: <c>CancelAsync</c> never reports success to a caller who cancelled, keeps the schedule
/// cancellable afterwards, and still treats a gone token as a no-op.
/// </summary>
public sealed class SchedulerCancellationTests
{
    [Fact]
    public async Task CancelAsync_CancelledToken_ThrowsOperationCanceled_AndTheSameTokenCanBeCancelledAgain()
    {
        var (scheduler, mt, token) = await ScheduledAsync();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        mt.CancelScheduledPublish(typeof(SchedulerTestMessage), token, Arg.Is<CancellationToken>(c => c.IsCancellationRequested))
            .ThrowsAsync(new OperationCanceledException(cancelled.Token));

        var act = async () => await scheduler.CancelAsync(token, cancelled.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();

        // The entry was restored, so a live token reaches the MassTransit scheduler exactly once.
        await scheduler.CancelAsync(token, CancellationToken.None);

        await mt.Received(1).CancelScheduledPublish(
            typeof(SchedulerTestMessage), token, Arg.Is<CancellationToken>(c => !c.IsCancellationRequested));
    }

    [Fact]
    public async Task CancelAsync_UnknownToken_IsANoOp()
    {
        var mt = Substitute.For<MtScheduler>();
        var scheduler = new MassTransitMessageScheduler(mt);

        await scheduler.CancelAsync(Guid.NewGuid(), CancellationToken.None);

        await mt.DidNotReceiveWithAnyArgs().CancelScheduledPublish(default!, default, default);
    }

    [Fact]
    public async Task CancelAsync_NonCancellationFault_IsStillSwallowed()
    {
        // Pinned so that narrowing the catch (SK.07.ObservabilityConformance) is a knowing change.
        var (scheduler, mt, token) = await ScheduledAsync();
        mt.CancelScheduledPublish(typeof(SchedulerTestMessage), token, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("token no longer tracked"));

        var act = async () => await scheduler.CancelAsync(token, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    private static async Task<(MassTransitMessageScheduler Scheduler, MtScheduler Mt, Guid Token)> ScheduledAsync()
    {
        var token = Guid.NewGuid();
        var scheduled = Substitute.For<global::MassTransit.ScheduledMessage<SchedulerTestMessage>>();
        scheduled.TokenId.Returns(token);

        var mt = Substitute.For<MtScheduler>();
        mt.SchedulePublish<SchedulerTestMessage>(Arg.Any<DateTime>(), Arg.Any<SchedulerTestMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(scheduled));

        var scheduler = new MassTransitMessageScheduler(mt);
        var issued = await scheduler.ScheduleAsync(
            new SchedulerTestMessage("later"), DateTimeOffset.UtcNow.AddMinutes(5), CancellationToken.None);
        issued.Should().Be(token);

        return (scheduler, mt, token);
    }
}

public sealed record SchedulerTestMessage(string Text);
