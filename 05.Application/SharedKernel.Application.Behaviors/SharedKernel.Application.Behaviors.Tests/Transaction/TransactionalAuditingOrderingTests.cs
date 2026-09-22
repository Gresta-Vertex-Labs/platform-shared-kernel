using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Application.Auditing;
using SharedKernel.Application.Behaviors.Auditing;
using SharedKernel.Application.Behaviors.Commands;
using SharedKernel.Application.Behaviors.Extensions;
using SharedKernel.Application.Behaviors.Tests.Support;
using SharedKernel.Application.Messaging;
using SharedKernel.Application.Transactions;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application.Behaviors.Tests.Transaction;

/// <summary>
/// Proves — through a real, composed <c>ServiceCollection</c> + <c>AddMediatR</c> +
/// <see cref="ApplicationBehaviorsBuilder"/> dispatch — how auditing sits around the transaction: the
/// <see cref="AuditOutcome.Succeeded"/> entry is written inside the transaction, after the business
/// save and before the commit (the unit of work's pre-commit hook); every failure — a failed
/// <c>Result</c>, a thrown exception, a failed commit — is written after the rollback.
/// </summary>
/// <remarks>
/// The real-PostgreSQL proof that the success entry commits atomically with the business write lives
/// in <c>SharedKernel.Persistence.EfCore.Auditing.Tests</c>; this test proves the ordering contract
/// deterministically, without a database.
/// </remarks>
public sealed class TransactionalAuditingOrderingTests
{
    private sealed record TestCommand(bool ShouldFail, bool ShouldThrow = false)
        : ICommand, IAuditableRequest<Result>
    {
        public string Action => "test.action";
        public string ResourceType => "TestResource";
        public string ResourceId => "r-1";
        public string? BeforeSnapshot => null;
        public string? GetAfterSnapshot(Result response) => response.IsSuccess ? "after" : null;
    }

    private sealed class TestCommandHandler(List<string> sequence, ICommandScope commandScope) : ICommandHandler<TestCommand>
    {
        public Task<Result> Handle(TestCommand request, CancellationToken cancellationToken)
        {
            if (request.ShouldThrow)
                throw new InvalidOperationException("boom");

            sequence.Add("handler");
            commandScope.OnCompleted(_ =>
            {
                sequence.Add("after-commit");
                return Task.CompletedTask;
            });

            return Task.FromResult(request.ShouldFail
                ? Result.Failure(Error.Validation("test.rejected", "Rejected for testing."))
                : Result.Success());
        }
    }

    private static (ServiceProvider Provider, List<string> Sequence, FakeUnitOfWork UnitOfWork, FakeAuditTrailWriter Writer) BuildProvider(
        Action<FakeUnitOfWork>? configure = null,
        bool withTransaction = true)
    {
        var sequence = new List<string>();
        var unitOfWork = new FakeUnitOfWork(sequence);
        configure?.Invoke(unitOfWork);
        var writer = new FakeAuditTrailWriter(sequence);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(sequence);
        services.AddSingleton<IUnitOfWork>(unitOfWork);
        services.AddSingleton<IAuditTrailWriter>(writer);

        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<TransactionalAuditingOrderingTests>());

        var builder = services.AddSharedKernelApplicationBehaviors().AddAuditingBehavior();
        if (withTransaction)
            builder.AddTransactionBehavior();
        builder.Build();

        return (services.BuildServiceProvider(), sequence, unitOfWork, writer);
    }

    [Fact]
    public async Task Dispatch_Success_WritesSucceededEntryAfterSaveAndBeforeCommit()
    {
        var (provider, sequence, unitOfWork, writer) = BuildProvider();
        using var _ = provider;

        var result = await provider.GetRequiredService<ISender>().Send(new TestCommand(ShouldFail: false));

        result.IsSuccess.Should().BeTrue();
        sequence.Should().Equal(
            "transaction.begin", "handler", "transaction.savechanges", "audit.record", "transaction.commit", "after-commit");
        writer.RecordedEntries.Should().ContainSingle().Which.Outcome.Should().Be(AuditOutcome.Succeeded);
        writer.RecordedEntries[0].AfterSnapshot.Should().Be("after");
        unitOfWork.CommitCount.Should().Be(1);
    }

    [Fact]
    public async Task Dispatch_ResultFailure_RollsBackThenWritesFailedEntry()
    {
        var (provider, sequence, _, writer) = BuildProvider();
        using var __ = provider;

        var result = await provider.GetRequiredService<ISender>().Send(new TestCommand(ShouldFail: true));

        result.IsFailure.Should().BeTrue();
        sequence.Should().Equal("transaction.begin", "handler", "transaction.rollback", "audit.record");
        var entry = writer.RecordedEntries.Should().ContainSingle().Subject;
        entry.Outcome.Should().Be(AuditOutcome.Failed);
        entry.ErrorCode.Should().Be("test.rejected");
    }

    [Fact]
    public async Task Dispatch_HandlerThrows_RollsBackThenWritesFaultEntryAndRethrows()
    {
        var (provider, sequence, _, writer) = BuildProvider();
        using var __ = provider;

        var act = async () => await provider.GetRequiredService<ISender>().Send(new TestCommand(ShouldFail: false, ShouldThrow: true));

        await act.Should().ThrowAsync<InvalidOperationException>();
        sequence.Should().Equal("transaction.begin", "transaction.rollback", "audit.record");
        writer.RecordedEntries.Should().ContainSingle().Which.Outcome.Should().Be(AuditOutcome.Failed);
    }

    [Fact]
    public async Task Dispatch_CommitFails_NoSucceededEntrySurvives_AndAFailedEntryIsWritten()
    {
        // The case the old in-transaction placement could never record: the handler succeeded but the
        // commit failed. The succeeded entry was written inside the transaction and rolled back with it.
        var (provider, sequence, _, writer) = BuildProvider(u => u.SimulateCommitFailure = true);
        using var __ = provider;

        var act = async () => await provider.GetRequiredService<ISender>().Send(new TestCommand(ShouldFail: false));

        await act.Should().ThrowAsync<InvalidOperationException>();
        sequence.Should().Equal(
            "transaction.begin", "handler", "transaction.savechanges", "audit.record", "transaction.rollback", "audit.record");
        writer.RecordedEntries.Select(e => e.Outcome).Should().Equal(AuditOutcome.Succeeded, AuditOutcome.Failed);
        writer.RecordedEntries[1].ErrorCode.Should().Be(typeof(InvalidOperationException).FullName);
        sequence.Should().NotContain("after-commit");
    }

    [Fact]
    public async Task Dispatch_TransientRetry_ReplaysHandler_ButAuditsAndRunsAfterCommitCallbacksOnce()
    {
        var (provider, sequence, unitOfWork, writer) = BuildProvider(u => u.TransientFailures = 1);
        using var _ = provider;

        var result = await provider.GetRequiredService<ISender>().Send(new TestCommand(ShouldFail: false));

        result.IsSuccess.Should().BeTrue();
        sequence.Count(s => s == "handler").Should().Be(2);
        sequence.Count(s => s == "after-commit").Should().Be(1, "callbacks queued by the discarded attempt are dropped");
        writer.RecordedEntries.Should().ContainSingle().Which.Outcome.Should().Be(AuditOutcome.Succeeded);
        unitOfWork.CommitCount.Should().Be(1);
    }

    [Fact]
    public async Task Dispatch_WithoutTransactionBehavior_WritesSucceededEntryDirectly()
    {
        var (provider, sequence, unitOfWork, writer) = BuildProvider(withTransaction: false);
        using var _ = provider;

        var result = await provider.GetRequiredService<ISender>().Send(new TestCommand(ShouldFail: false));

        result.IsSuccess.Should().BeTrue();
        sequence.Should().Equal("handler", "audit.record", "after-commit");
        writer.RecordedEntries.Should().ContainSingle().Which.Outcome.Should().Be(AuditOutcome.Succeeded);
        unitOfWork.CommitCount.Should().Be(0);
    }
}
