using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Application.Behaviors.Auditing;
using SharedKernel.Application.Behaviors.Extensions;
using SharedKernel.Application.Behaviors.Tests.Support;
using SharedKernel.Application.Behaviors.Transaction;
using SharedKernel.Application.Messaging;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application.Behaviors.Tests.Transaction;

/// <summary>
/// Proves — through a real, composed <c>ServiceCollection</c> + <c>AddMediatR</c> +
/// <see cref="ApplicationBehaviorsBuilder"/> dispatch, never a hand-rolled pipeline — that when the
/// resolved <c>IUnitOfWork</c> also implements <c>ITransactionalUnitOfWork</c>,
/// <c>TransactionBehavior</c> opens the transaction BEFORE <c>AuditingBehavior</c> (registered inner to
/// it in the canonical command stage) records its entry, and only commits or rolls back after that
/// entry has already been staged.
/// </summary>
/// <remarks>
/// This is the exact defect this capability fixes: an audit writer that requires an ambient
/// transaction to record a successful outcome previously had none to enlist in, because nothing in
/// <c>05.Application.Behaviors</c>/<c>13.ServiceDefaults</c> ever opened one. The genuinely atomic,
/// real-Postgres proof of that fix lives in
/// <c>13.ServiceDefaults.Persistence.Tests</c>' <c>AuditTransactionWiringPostgresTests</c> — this test
/// proves the ORDERING contract the fix depends on, fast and deterministically, without a database.
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

    private sealed class TestCommandHandler(List<string> sequence) : ICommandHandler<TestCommand>
    {
        public Task<Result> Handle(TestCommand request, CancellationToken cancellationToken)
        {
            if (request.ShouldThrow)
                throw new InvalidOperationException("boom");

            sequence.Add("handler");
            return Task.FromResult(request.ShouldFail
                ? Result.Failure(Error.Validation("test.rejected", "Rejected for testing."))
                : Result.Success());
        }
    }

    private static (ServiceProvider Provider, List<string> Sequence, FakeTransactionalUnitOfWork UnitOfWork) BuildProvider()
    {
        var sequence = new List<string>();
        var unitOfWork = new FakeTransactionalUnitOfWork(sequence);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(sequence);
        services.AddSingleton<IUnitOfWork>(unitOfWork);
        services.AddSingleton<IAuditTrailWriter>(new FakeAuditTrailWriter(sequence));

        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<TransactionalAuditingOrderingTests>());

        services.AddSharedKernelApplicationBehaviors()
            .AddAuditingBehavior()
            .AddTransactionBehavior()
            .Build();

        return (services.BuildServiceProvider(), sequence, unitOfWork);
    }

    [Fact]
    public async Task Dispatch_Success_OpensTransactionBeforeAuditRecordAndCommitsAfterSaveChanges()
    {
        var (provider, sequence, unitOfWork) = BuildProvider();
        using var _ = provider;
        var sender = provider.GetRequiredService<ISender>();

        var result = await sender.Send(new TestCommand(ShouldFail: false));

        result.IsSuccess.Should().BeTrue();
        sequence.Should().Equal(
            "transaction.begin", "handler", "audit.record", "transaction.savechanges", "transaction.commit");
        unitOfWork.LastTransaction!.IsCommitted.Should().BeTrue();
        unitOfWork.LastTransaction.IsRolledBack.Should().BeFalse();
    }

    [Fact]
    public async Task Dispatch_ResultFailure_RecordsFailedAuditEntryInsideTransactionThenRollsBackWithoutSaving()
    {
        var (provider, sequence, unitOfWork) = BuildProvider();
        using var _ = provider;
        var sender = provider.GetRequiredService<ISender>();

        var result = await sender.Send(new TestCommand(ShouldFail: true));

        result.IsFailure.Should().BeTrue();
        sequence.Should().Equal("transaction.begin", "handler", "audit.record", "transaction.rollback");
        sequence.Should().NotContain("transaction.savechanges");
        unitOfWork.LastTransaction!.IsRolledBack.Should().BeTrue();
        unitOfWork.LastTransaction.IsCommitted.Should().BeFalse();
    }

    [Fact]
    public async Task Dispatch_HandlerThrows_RecordsFaultAuditEntryInsideTransactionThenRollsBackAndRethrows()
    {
        var (provider, sequence, unitOfWork) = BuildProvider();
        using var _ = provider;
        var sender = provider.GetRequiredService<ISender>();

        var act = async () => await sender.Send(new TestCommand(ShouldFail: false, ShouldThrow: true));

        await act.Should().ThrowAsync<InvalidOperationException>();
        sequence.Should().Equal("transaction.begin", "audit.record", "transaction.rollback");
        sequence.Should().NotContain("transaction.savechanges");
        unitOfWork.LastTransaction!.IsRolledBack.Should().BeTrue();
        unitOfWork.LastTransaction.IsCommitted.Should().BeFalse();
    }
}
