using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SharedKernel.Messaging.MassTransit.Diagnostics;
using SharedKernel.Messaging.MassTransit.MessageBus;
using SharedKernel.Testing.Logging;
using MtScheduler = MassTransit.IMessageScheduler;

namespace SharedKernel.Messaging.MassTransit.Tests.ErrorSafetyTests;

/// <summary>
/// <c>CancelAsync</c> never reports success to a caller who cancelled, keeps the schedule cancellable afterwards, still
/// treats a gone token as a no-op, swallows only a recognised transport fault (logging it) and lets any other exception
/// propagate.
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
        var scheduler = new MassTransitMessageScheduler(mt, NullLogger<MassTransitMessageScheduler>.Instance);

        await scheduler.CancelAsync(Guid.NewGuid(), CancellationToken.None);

        await mt.DidNotReceiveWithAnyArgs().CancelScheduledPublish(default!, default, default);
    }

    [Fact]
    public async Task CancelAsync_RecognisedTransportFault_IsSwallowedAndLoggedWithTheException()
    {
        const string brokerHost = "broker.sentinel-host.internal";
        var logger = new InMemoryLogger<MassTransitMessageScheduler>();
        var (scheduler, mt, token) = await ScheduledAsync(logger);
        var fault = new global::MassTransit.TransportUnavailableException($"connection to {brokerHost} refused");
        mt.CancelScheduledPublish(typeof(SchedulerTestMessage), token, Arg.Any<CancellationToken>()).ThrowsAsync(fault);

        var act = async () => await scheduler.CancelAsync(token, CancellationToken.None);

        await act.Should().NotThrowAsync();

        var record = logger.Records.ShouldHaveLogged(new EventId(MessagingLoggingEventIds.SchedulerCancelFailed), LogLevel.Warning);
        record.Exception.Should().BeSameAs(fault);
        logger.Records.ShouldHaveLoggedWithException(new EventId(MessagingLoggingEventIds.SchedulerCancelFailed), typeof(global::MassTransit.TransportUnavailableException));
        // The exception object travels with the record (L4) but its text is never part of the message or its properties.
        logger.Records.Should().OnlyContain(r => !r.Message.Contains(brokerHost, StringComparison.Ordinal));
        logger.Records.ShouldFollowLogConvention();
    }

    [Fact]
    public async Task CancelAsync_UnrecognisedException_Propagates_AndTheTokenStaysCancellable()
    {
        var (scheduler, mt, token) = await ScheduledAsync();
        mt.CancelScheduledPublish(typeof(SchedulerTestMessage), token, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("a bug, not a transport fault"));

        var act = async () => await scheduler.CancelAsync(token, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("a bug, not a transport fault");

        // The entry was restored, so the same token reaches the scheduler again.
        await act.Should().ThrowAsync<InvalidOperationException>();
        await mt.Received(2).CancelScheduledPublish(typeof(SchedulerTestMessage), token, Arg.Any<CancellationToken>());
    }

    private static async Task<(MassTransitMessageScheduler Scheduler, MtScheduler Mt, Guid Token)> ScheduledAsync(
        ILogger<MassTransitMessageScheduler>? logger = null)
    {
        var token = Guid.NewGuid();
        var scheduled = Substitute.For<global::MassTransit.ScheduledMessage<SchedulerTestMessage>>();
        scheduled.TokenId.Returns(token);

        var mt = Substitute.For<MtScheduler>();
        mt.SchedulePublish<SchedulerTestMessage>(Arg.Any<DateTime>(), Arg.Any<SchedulerTestMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(scheduled));

        var scheduler = new MassTransitMessageScheduler(mt, logger ?? NullLogger<MassTransitMessageScheduler>.Instance);
        var issued = await scheduler.ScheduleAsync(
            new SchedulerTestMessage("later"), DateTimeOffset.UtcNow.AddMinutes(5), CancellationToken.None);
        issued.Should().Be(token);

        return (scheduler, mt, token);
    }
}

public sealed record SchedulerTestMessage(string Text);
