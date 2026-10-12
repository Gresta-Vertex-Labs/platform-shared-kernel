using System.Diagnostics;
using FluentAssertions;
using SharedKernel.Core.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Application.Messaging;
using SharedKernel.Application.Pipeline.Tests.Support;
using SharedKernel.Application.Pipeline.Transaction;
using SharedKernel.Execution.Transactions;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Testing.Logging;
using SharedKernel.Testing.Telemetry;

namespace SharedKernel.Application.Pipeline.Tests.Transaction;

public sealed class TransactionTelemetryTests
{
    private const string SentinelFaultText = "sentinel-transaction-fault-2b8c";

    private enum Mode
    {
        Ok,
        Rejected,
        Throws,
        Canceled,
    }

    private sealed record Work(Mode Mode) : ICommand;

    private sealed record Outer(bool InnerFails) : ICommand;

    private sealed record Inner(bool Fails) : ICommand;

    private sealed class SentinelFault() : InvalidOperationException(SentinelFaultText);

    private sealed class Journal
    {
        public Exception? Thrown { get; set; }
    }

    private sealed class WorkHandler(Journal journal) : ICommandHandler<Work>
    {
        public Task<Result> Handle(Work request, CancellationToken cancellationToken)
        {
            switch (request.Mode)
            {
                case Mode.Rejected:
                    return Task.FromResult(Result.Failure(Error.BusinessRule("test.rejected", "Rejected for testing.")));
                case Mode.Throws:
                    journal.Thrown = new SentinelFault();
                    throw journal.Thrown;
                case Mode.Canceled:
                    throw new OperationCanceledException();
                default:
                    return Task.FromResult(Result.Success());
            }
        }
    }

    private sealed class OuterHandler(ISender sender) : ICommandHandler<Outer>
    {
        public async Task<Result> Handle(Outer request, CancellationToken cancellationToken)
        {
            // The outer handler deliberately ignores the nested outcome; the unit of work refuses to commit it.
            _ = await sender.Send(new Inner(request.InnerFails), cancellationToken);
            return Result.Success();
        }
    }

    private sealed class InnerHandler : ICommandHandler<Inner>
    {
        public Task<Result> Handle(Inner request, CancellationToken cancellationToken)
            => Task.FromResult(request.Fails ? Result.Failure(Error.BusinessRule("test.inner_rejected", "Rejected.")) : Result.Success());
    }

    private static (ServiceProvider Provider, FakeUnitOfWork UnitOfWork, Journal Journal) Build(Action<FakeUnitOfWork>? configure = null)
    {
        var journal = new Journal();
        var unitOfWork = new FakeUnitOfWork();
        configure?.Invoke(unitOfWork);
        var services = new ServiceCollection();
        services.AddInMemoryLoggerFactory();
        services.AddSingleton(journal);
        services.AddSingleton<IUnitOfWork>(unitOfWork);
        services.AddSharedKernelApplication(typeof(TransactionTelemetryTests).Assembly, app => app.UseMediatR().WithTransactions());
        return (services.BuildServiceProvider(), unitOfWork, journal);
    }

    [Fact]
    public async Task Commit_RecordsOneAttemptNotJoinedAndSuccess()
    {
        using var probe = new TelemetryProbe();
        var (provider, _, _) = Build();
        using var host = provider;

        var result = await provider.GetRequiredService<ISender>().Send(new Work(Mode.Ok));

        result.IsSuccess.Should().BeTrue();
        probe.Spans.ShouldHaveSingleSpan("application transaction")
            .ShouldHaveKind(ActivityKind.Internal)
            .ShouldHaveSource("SharedKernel.Application")
            .ShouldHaveTag("application.attempts", 1)
            .ShouldHaveTag("application.transaction_joined", false)
            .ShouldHaveOutcome("success")
            .ShouldBeRecordedSuccess();
        probe.Spans.Where(span => span.OperationName.StartsWith("application ", StringComparison.Ordinal)).ShouldFollowSpanConvention();
        probe.Records(provider).Where(record => record.EventId.Id is >= 5160 and <= 5162).Should().BeEmpty();
    }

    [Fact]
    public async Task FailedResult_RecordsTheFailureShapeAndLogsTheRollbackAsAWarning()
    {
        using var probe = new TelemetryProbe();
        var (provider, _, _) = Build();
        using var host = provider;

        var result = await provider.GetRequiredService<ISender>().Send(new Work(Mode.Rejected));

        result.IsFailure.Should().BeTrue();
        probe.Spans.ShouldHaveSingleSpan("application transaction")
            .ShouldBeRecordedFailure(Error.BusinessRule("test.rejected", "Rejected for testing."))
            .ShouldHaveOutcome("failure")
            .ShouldHaveTag("application.attempts", 1);
        var log = probe.Records(provider).ShouldHaveLogged(new EventId(5160), LogLevel.Warning);
        log.TryGetProperty("ErrorCode", out var code).Should().BeTrue();
        code.Should().Be("test.rejected");
        log.Exception.Should().BeNull();
    }

    [Fact]
    public async Task ThrowingHandler_RecordsTheExceptionShapeLogsWithTheExceptionAndRethrowsTheSameInstance()
    {
        using var probe = new TelemetryProbe();
        var (provider, _, journal) = Build();
        using var host = provider;

        var act = () => provider.GetRequiredService<ISender>().Send(new Work(Mode.Throws));

        (await act.Should().ThrowAsync<SentinelFault>()).Which.Should().BeSameAs(journal.Thrown);
        probe.Spans.ShouldHaveSingleSpan("application transaction")
            .ShouldBeRecordedException(typeof(SentinelFault))
            .ShouldHaveOutcome("exception")
            .ShouldHaveTag("application.attempts", 1);
        var records = probe.Records(provider);
        records.ShouldHaveLogged(new EventId(5161), LogLevel.Error).Exception.Should().BeSameAs(journal.Thrown);
        foreach (var record in records)
        {
            record.Message.Should().NotContain(SentinelFaultText);
            record.Template.Should().NotContain(SentinelFaultText);
        }
    }

    [Fact]
    public async Task Cancellation_IsNeitherAnErrorStatusNorAnErrorLog()
    {
        using var probe = new TelemetryProbe();
        var (provider, _, _) = Build();
        using var host = provider;

        var act = () => provider.GetRequiredService<ISender>().Send(new Work(Mode.Canceled));

        await act.Should().ThrowAsync<OperationCanceledException>();
        probe.Spans.ShouldHaveSingleSpan("application transaction").ShouldBeRecordedCancellation().ShouldHaveOutcome("canceled");
        probe.Records(provider).Where(record => record.EventId.Id is >= 5160 and <= 5162).Should().BeEmpty();
    }

    [Fact]
    public async Task UnitOfWorkReplaysTheDelegate_CountsAttemptsAndLogsTheReplayOnce()
    {
        using var probe = new TelemetryProbe();
        var (provider, unitOfWork, _) = Build(uow => uow.TransientFailures = 1);
        using var host = provider;

        var result = await provider.GetRequiredService<ISender>().Send(new Work(Mode.Ok));

        result.IsSuccess.Should().BeTrue();
        unitOfWork.AttemptCount.Should().Be(2);
        probe.Spans.ShouldHaveSingleSpan("application transaction")
            .ShouldHaveTag("application.attempts", 2)
            .ShouldHaveOutcome("success");
        var replays = probe.Records(provider).Where(record => record.EventId.Id == 5162).ToList();
        replays.Should().ContainSingle().Which.LogLevel.Should().Be(LogLevel.Warning);
        replays[0].TryGetProperty("AttemptCount", out var attempts).Should().BeTrue();
        attempts.Should().Be(2);
    }

    [Fact]
    public async Task JoinedCommand_IsTaggedJoinedAndRecordsItsOwnFailure_WhileTheOwnerLogsTheRollback()
    {
        using var probe = new TelemetryProbe();
        var (provider, unitOfWork, _) = Build();
        using var host = provider;

        var act = () => provider.GetRequiredService<ISender>().Send(new Outer(InnerFails: true));

        await act.Should().ThrowAsync<TransactionRolledBackException>();
        unitOfWork.CommitCount.Should().Be(0);
        var spans = probe.Spans.Where(span => span.OperationName == "application transaction").ToList();
        spans.Should().HaveCount(2);
        var joined = spans.Single(span => Equals(span.GetTagItem("application.transaction_joined"), true));
        var owner = spans.Single(span => Equals(span.GetTagItem("application.transaction_joined"), false));
        joined.ShouldBeRecordedFailure(Error.BusinessRule("test.inner_rejected", "Rejected."));
        owner.ShouldBeRecordedException(typeof(TransactionRolledBackException));

        var records = probe.Records(provider);
        records.Where(record => record.EventId.Id == 5160).Should().BeEmpty("a joined command leaves the rollback to its owner");
        records.ShouldHaveLogged(new EventId(5161), LogLevel.Error);
    }

    [Fact]
    public async Task ClassifiedExceptionFromTheUnitOfWork_PutsItsCodeOnTheSpan()
    {
        using var probe = new TelemetryProbe();
        var commitFault = Error.Unavailable("test.commit_unknown", "The commit outcome is unknown.").ToException();
        var (provider, _, _) = Build(uow =>
        {
            uow.SimulateCommitFailure = true;
            uow.CommitFault = commitFault;
        });
        using var host = provider;

        var act = () => provider.GetRequiredService<ISender>().Send(new Work(Mode.Ok));

        (await act.Should().ThrowAsync<Exception>()).Which.Should().BeSameAs(commitFault);
        probe.Spans.ShouldHaveSingleSpan("application transaction")
            .ShouldBeRecordedException(commitFault.GetType(), "test.commit_unknown");
    }

    [Fact]
    public async Task NestedCommandWithoutAnActiveTransaction_CallsNextDirectlyWithNoSpan()
    {
        using var probe = new TelemetryProbe();
        var unitOfWork = new FakeUnitOfWork();
        var behavior = new TransactionBehavior<Work, Result>(
            unitOfWork,
            new FakeCommandScope(isNested: true),
            NullLogger<TransactionBehavior<Work, Result>>.Instance);

        var result = await behavior.Handle(new Work(Mode.Ok), () => Task.FromResult(Result.Success()), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        probe.Spans.ShouldNotHaveSpan("application transaction");
    }

    [Fact]
    public async Task TransactionLogEvents_FollowTheLogConventionsInThePipelineSubBlock()
    {
        using var probe = new TelemetryProbe();
        var (provider, _, _) = Build(uow => uow.TransientFailures = 1);
        using var host = provider;
        var sender = provider.GetRequiredService<ISender>();

        await sender.Send(new Work(Mode.Ok));
        await sender.Send(new Work(Mode.Rejected));
        await FluentActions.Awaiting(() => sender.Send(new Work(Mode.Throws))).Should().ThrowAsync<SentinelFault>();

        IReadOnlyList<LogRecord> records =
            [.. probe.Records(provider).Where(record => record.EventId.Id is >= 5160 and <= 5162)];
        records.Select(record => record.EventId.Id).Should().BeEquivalentTo([5160, 5161, 5162]);
        records.ShouldFollowLogConvention()
            .ShouldOnlyLogEventIdsInSubBlock(SharedKernel.Primitives.Logging.LoggingEventIdRanges.Application, 1);
    }
}
