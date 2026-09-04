using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Application.Behaviors.Auditing;
using SharedKernel.Application.Behaviors.Extensions;
using SharedKernel.Application.Behaviors.Transaction;
using SharedKernel.Application.Messaging;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application.Behaviors.Tests.Auditing;

/// <summary>
/// A genuine TEMPORAL proof — beyond mere registration-list order — that
/// <see cref="IAuditTrailWriter.RecordAsync"/> is observably called BEFORE
/// <see cref="IUnitOfWork.SaveChangesAsync"/> for the same request, dispatched through the real
/// <see cref="ApplicationBehaviorsBuilder"/>-built pipeline (WO-071, T-77).
/// </summary>
public sealed class AuditingTransactionOrderingTests
{
    private sealed record TestCommand : ICommand, IAuditableRequest<Result>
    {
        public string Action => "test.action";
        public string ResourceType => "TestResource";
        public string ResourceId => "resource-1";
        public string? BeforeSnapshot => null;
        public string? GetAfterSnapshot(Result response) => "after";
    }

    private sealed class TestCommandHandler : IRequestHandler<TestCommand, Result>
    {
        public Task<Result> Handle(TestCommand request, CancellationToken cancellationToken)
            => Task.FromResult(Result.Success());
    }

    /// <summary>Records the order in which <see cref="RecordAsync"/> and <see cref="SaveChangesAsync"/> fire.</summary>
    private sealed class OrderRecordingSpy : IAuditTrailWriter, IUnitOfWork
    {
        public List<string> CallOrder { get; } = [];

        public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            CallOrder.Add(nameof(RecordAsync));
            return Task.CompletedTask;
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        {
            CallOrder.Add(nameof(SaveChangesAsync));
            return Task.FromResult(1);
        }
    }

    [Fact]
    public async Task Handle_AuditingAndTransactionBothRegistered_RecordAsyncObservablyPrecedesSaveChangesAsync()
    {
        var spy = new OrderRecordingSpy();
        var services = new ServiceCollection();
        services.AddSingleton<IAuditTrailWriter>(spy);
        services.AddSingleton<IUnitOfWork>(spy);
        services.AddSingleton<TestCommandHandler>();
        services.AddSingleton<IRequestHandler<TestCommand, Result>>(
            sp => sp.GetRequiredService<TestCommandHandler>());

        services
            .AddSharedKernelApplicationBehaviors()
            .AddAuditingBehavior()
            .AddTransactionBehavior()
            .Build();

        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<AuditingTransactionOrderingTests>());
        var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<ISender>();

        var result = await sender.Send(new TestCommand());

        result.IsSuccess.Should().BeTrue();
        spy.CallOrder.Should().Equal(
            [nameof(IAuditTrailWriter.RecordAsync), nameof(IUnitOfWork.SaveChangesAsync)],
            "AuditingBehavior must be registered CLOSER to the handler than TransactionBehavior so its " +
            "audit write completes BEFORE TransactionBehavior's own commit executes — 'just inside " +
            "Transaction', the temporal mirror-image of CacheInvalidationBehavior's post-commit-only " +
            "positioning");
    }
}
