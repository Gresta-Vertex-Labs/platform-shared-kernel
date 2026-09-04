using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Application.Behaviors.Auditing;
using SharedKernel.Application.Behaviors.CacheInvalidation;
using SharedKernel.Application.Behaviors.Extensions;
using SharedKernel.Application.Behaviors.Transaction;
using SharedKernel.Application.Messaging;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application.Behaviors.Tests.CacheInvalidation;

/// <summary>
/// A genuine TEMPORAL proof — beyond mere registration-list order — that
/// <see cref="CacheInvalidationBehavior{TRequest,TResponse}"/>'s cache eviction is observably
/// called AFTER <see cref="IUnitOfWork.SaveChangesAsync"/> for the same request, dispatched
/// through the real <see cref="ApplicationBehaviorsBuilder"/>-built pipeline (WO-080, P-488).
/// </summary>
/// <remarks>
/// Mirrors <c>Auditing.AuditingTransactionOrderingTests</c>' exact shape (WO-071, T-77) — the
/// technique that caught the sibling defect in the Auditing/Transaction pair is applied here to
/// the Transaction/CacheInvalidation pair. Prior to WO-080, <c>CacheInvalidationBehavior</c> was
/// registered AFTER <c>TransactionBehavior</c> in <c>ApplicationBehaviorsBuilder.Build()</c>,
/// which made eviction run BEFORE the commit — a confirmed defect discovered as a byproduct of
/// the WO-071 session but left unfixed as out of that phase's scope. This file's tests were
/// verified, before the fix landed, to FAIL against the pre-fix registration order (eviction
/// observed before the commit) — a non-vacuous regression proof, not merely a test that happens
/// to pass either way.
/// </remarks>
public sealed class CacheInvalidationTransactionOrderingTests
{
    private sealed record InvalidatingCommand : ICommand, IInvalidatesCache
    {
        public IReadOnlyCollection<string> CacheKeysToInvalidate => ["key-1"];
        public IReadOnlyCollection<string> CacheTagsToInvalidate => ["tag-1"];
    }

    private sealed record AuditedInvalidatingCommand : ICommand, IInvalidatesCache, IAuditableRequest<Result>
    {
        public IReadOnlyCollection<string> CacheKeysToInvalidate => ["key-2"];
        public IReadOnlyCollection<string> CacheTagsToInvalidate => ["tag-2"];
        public string Action => "test.action";
        public string ResourceType => "TestResource";
        public string ResourceId => "resource-1";
        public string? BeforeSnapshot => null;
        public string? GetAfterSnapshot(Result response) => "after";
    }

    private sealed class InvalidatingCommandHandler : IRequestHandler<InvalidatingCommand, Result>
    {
        public Task<Result> Handle(InvalidatingCommand request, CancellationToken cancellationToken)
            => Task.FromResult(Result.Success());
    }

    private sealed class AuditedInvalidatingCommandHandler : IRequestHandler<AuditedInvalidatingCommand, Result>
    {
        public Task<Result> Handle(AuditedInvalidatingCommand request, CancellationToken cancellationToken)
            => Task.FromResult(Result.Success());
    }

    /// <summary>
    /// Records the order in which <see cref="RemoveAsync"/>/<see cref="RemoveByTagAsync"/>,
    /// <see cref="SaveChangesAsync"/>, and (when wired in) <see cref="RecordAsync"/> fire.
    /// </summary>
    private sealed class OrderRecordingSpy : ICacheService, IUnitOfWork, IAuditTrailWriter
    {
        public List<string> CallOrder { get; } = [];

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        {
            CallOrder.Add(nameof(SaveChangesAsync));
            return Task.FromResult(1);
        }

        public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            CallOrder.Add(nameof(RecordAsync));
            return Task.CompletedTask;
        }

        public ValueTask RemoveAsync(string key, CancellationToken ct = default)
        {
            CallOrder.Add(nameof(RemoveAsync));
            return ValueTask.CompletedTask;
        }

        public ValueTask RemoveByTagAsync(string tag, CancellationToken ct = default)
        {
            CallOrder.Add(nameof(RemoveByTagAsync));
            return ValueTask.CompletedTask;
        }

        public ValueTask<T?> GetAsync<T>(string key, CancellationToken ct = default)
            => throw new NotSupportedException("Not exercised by this ordering test.");

        public ValueTask SetAsync<T>(string key, T value, CachePolicy policy, CancellationToken ct = default)
            => throw new NotSupportedException("Not exercised by this ordering test.");

        public ValueTask<T> GetOrSetAsync<T>(
            string key,
            Func<CancellationToken, ValueTask<T>> factory,
            CachePolicy policy,
            CancellationToken ct = default)
            => throw new NotSupportedException("Not exercised by this ordering test.");

        public ValueTask<IReadOnlyDictionary<string, T?>> GetManyAsync<T>(
            IEnumerable<string> keys,
            CancellationToken ct = default)
            => throw new NotSupportedException("Not exercised by this ordering test.");

        public ValueTask SetManyAsync<T>(
            IReadOnlyDictionary<string, T> entries,
            CachePolicy policy,
            CancellationToken ct = default)
            => throw new NotSupportedException("Not exercised by this ordering test.");
    }

    /// <summary>
    /// The default, documented registration path: only CacheInvalidation and Transaction opted in.
    /// </summary>
    [Fact]
    public async Task Handle_CacheInvalidationAndTransactionBothRegistered_EvictionObservablyFollowsSaveChangesAsync()
    {
        var spy = new OrderRecordingSpy();
        var services = new ServiceCollection();
        services.AddSingleton<ICacheService>(spy);
        services.AddSingleton<IUnitOfWork>(spy);
        services.AddSingleton<InvalidatingCommandHandler>();
        services.AddSingleton<IRequestHandler<InvalidatingCommand, Result>>(
            sp => sp.GetRequiredService<InvalidatingCommandHandler>());

        services
            .AddSharedKernelApplicationBehaviors()
            .AddCacheInvalidationBehavior()
            .AddTransactionBehavior()
            .Build();

        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<CacheInvalidationTransactionOrderingTests>());
        var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<ISender>();

        var result = await sender.Send(new InvalidatingCommand());

        result.IsSuccess.Should().BeTrue();
        spy.CallOrder.Should().Equal(
            [nameof(IUnitOfWork.SaveChangesAsync), nameof(ICacheService.RemoveAsync), nameof(ICacheService.RemoveByTagAsync)],
            "CacheInvalidationBehavior must be registered CLOSER to the outer edge of the pipeline than " +
            "TransactionBehavior so its eviction call observably follows TransactionBehavior's own commit " +
            "— eviction must follow a confirmed commit, never a speculative one");
    }

    /// <summary>
    /// The full three-behavior registration path (Auditing + Transaction + CacheInvalidation),
    /// proving BOTH documented invariants hold simultaneously: Auditing's write lands inside the
    /// same commit, and CacheInvalidation's eviction follows that commit. This is also the proof
    /// that fixing the CacheInvalidation/Transaction pair (WO-080) did not disturb the already-
    /// correct Auditing/Transaction pair (WO-071/P-458).
    /// </summary>
    [Fact]
    public async Task Handle_AuditingTransactionAndCacheInvalidationAllRegistered_OrderIsRecordThenSaveThenEvict()
    {
        var spy = new OrderRecordingSpy();
        var services = new ServiceCollection();
        services.AddSingleton<ICacheService>(spy);
        services.AddSingleton<IUnitOfWork>(spy);
        services.AddSingleton<IAuditTrailWriter>(spy);
        services.AddSingleton<AuditedInvalidatingCommandHandler>();
        services.AddSingleton<IRequestHandler<AuditedInvalidatingCommand, Result>>(
            sp => sp.GetRequiredService<AuditedInvalidatingCommandHandler>());

        services
            .AddSharedKernelApplicationBehaviors()
            .AddAuditingBehavior()
            .AddTransactionBehavior()
            .AddCacheInvalidationBehavior()
            .Build();

        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<CacheInvalidationTransactionOrderingTests>());
        var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<ISender>();

        var result = await sender.Send(new AuditedInvalidatingCommand());

        result.IsSuccess.Should().BeTrue();
        spy.CallOrder.Should().Equal(
            [
                nameof(IAuditTrailWriter.RecordAsync),
                nameof(IUnitOfWork.SaveChangesAsync),
                nameof(ICacheService.RemoveAsync),
                nameof(ICacheService.RemoveByTagAsync)
            ],
            "the audit write must land inside the same commit (RecordAsync before SaveChangesAsync, " +
            "WO-071/P-458, unchanged by this fix), and cache eviction must only follow a CONFIRMED " +
            "commit (SaveChangesAsync before RemoveAsync/RemoveByTagAsync, WO-080/P-488)");
    }

    /// <summary>
    /// Proves the corrected temporal order holds for every <c>.AddXBehavior()</c> call-order
    /// permutation that includes both CacheInvalidation and Transaction, not just the one call
    /// order exercised by the two tests above — <see cref="ApplicationBehaviorsBuilder.Build"/>'s
    /// whole reason to exist is that the fixed canonical order is independent of call order.
    /// </summary>
    [Theory]
    [MemberData(nameof(CallOrderPermutations))]
    public async Task Handle_AcrossEveryAddXBehaviorCallOrderPermutation_EvictionAlwaysFollowsSaveChangesAsync(
        Action<ApplicationBehaviorsBuilder> configureInOrder)
    {
        var spy = new OrderRecordingSpy();
        var services = new ServiceCollection();
        services.AddSingleton<ICacheService>(spy);
        services.AddSingleton<IUnitOfWork>(spy);
        services.AddSingleton<InvalidatingCommandHandler>();
        services.AddSingleton<IRequestHandler<InvalidatingCommand, Result>>(
            sp => sp.GetRequiredService<InvalidatingCommandHandler>());

        var builder = services.AddSharedKernelApplicationBehaviors();
        configureInOrder(builder);
        builder.Build();

        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<CacheInvalidationTransactionOrderingTests>());
        var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<ISender>();

        await sender.Send(new InvalidatingCommand());

        var saveIndex = spy.CallOrder.IndexOf(nameof(IUnitOfWork.SaveChangesAsync));
        var removeIndex = spy.CallOrder.IndexOf(nameof(ICacheService.RemoveAsync));

        saveIndex.Should().BeGreaterThanOrEqualTo(0);
        removeIndex.Should().BeGreaterThanOrEqualTo(0);
        saveIndex.Should().BeLessThan(
            removeIndex,
            "regardless of .AddXBehavior() call order, Build() must always register " +
            "CacheInvalidationBehavior so its eviction observably follows TransactionBehavior's commit");
    }

    public static TheoryData<Action<ApplicationBehaviorsBuilder>> CallOrderPermutations()
    {
        return new TheoryData<Action<ApplicationBehaviorsBuilder>>
        {
            b => b.AddCacheInvalidationBehavior().AddTransactionBehavior(),
            b => b.AddTransactionBehavior().AddCacheInvalidationBehavior(),
        };
    }
}
