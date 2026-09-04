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
using Xunit;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>
/// Root Phase Backlog P-489 (WO-080): a genuinely EXECUTED real-assembly lock — mirroring
/// <c>SK.00.CacheEncryptionAndRedisValidationLock</c>'s T-337 "Technique A" precedent (the
/// composed-pipeline test living directly in <c>SecureDefaultsAssertionTests.cs</c>) — proving,
/// against the actual real, compiled <c>SharedKernel.Application.Behaviors</c> assembly's
/// <see cref="ApplicationBehaviorsBuilder"/>, that <see cref="CacheInvalidationBehavior{TRequest,TResponse}"/>'s
/// cache eviction executes only AFTER <see cref="IUnitOfWork.SaveChangesAsync"/>'s commit.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why this lives in 00.Governance, not only in 05.Application's own test suite.</strong>
/// <c>05.Application/SharedKernel.Application.Behaviors.Tests/CacheInvalidation/
/// CacheInvalidationTransactionOrderingTests.cs</c> already proves this exact fact in-domain
/// (WO-080, P-488) — this test is a deliberately INDEPENDENT, cross-domain proof on top of it, not
/// a duplicate. The root <c>state-map.md</c>'s own framing for P-489 is explicit about why: this
/// defect class has now bitten the platform twice (<c>AuditingBehavior</c> during P-458,
/// <c>CacheInvalidationBehavior</c> here), and both times a design document's canonical step
/// numbering silently diverged from <see cref="ApplicationBehaviorsBuilder.Build"/>'s actual DI
/// registration order with nothing mechanical catching it — "05.Application's own state-map showed
/// every phase key ● Complete while this defect sat unshipped and untracked." A lock that lives
/// only inside the domain whose own edit could reintroduce the defect is not an independent guard;
/// this test survives even a future, well-intentioned edit to
/// <c>CacheInvalidationTransactionOrderingTests.cs</c> itself (weakened, deleted, or silently
/// broken) because it is owned by a different domain entirely and consumes the real, compiled
/// <c>SharedKernel.Application.Behaviors.dll</c> via a test-only <c>ProjectReference</c>
/// (<c>PrivateAssets="all"</c> — see <c>SharedKernel.ArchitectureTests.Tests.csproj</c>), never a
/// source link or a hand-rolled substitute pipeline.
/// </para>
/// <para>
/// <strong>Deliberate departure from this project's IL-only discipline</strong> — same class of
/// departure as T-337/T-336: "eviction observably follows the commit" is an EMERGENT RUNTIME
/// PROPERTY of MediatR's onion-wrapping order, not something a static Mono.Cecil IL walk over
/// <see cref="ApplicationBehaviorsBuilder.Build"/>'s method body could honestly prove — the method
/// body's own physical top-to-bottom statement order does not equal temporal execution order for a
/// post-<c>next()</c> side effect (see that method's own extensive "DI-REGISTRATION-ORDER-TO-ONION-
/// ORDER RELATIONSHIP" code comment). This test therefore genuinely builds a real
/// <see cref="IServiceCollection"/>, calls the real <c>AddSharedKernelApplicationBehaviors()
/// .AddCacheInvalidationBehavior().AddTransactionBehavior().Build()</c> chain, registers the real
/// MediatR pipeline, and dispatches a real command through it end to end via <see cref="ISender"/>
/// — never a hand-rolled substitute pipeline (P-489 acceptance criterion 1).
/// </para>
/// <para>
/// <strong>Verified NON-VACUOUS</strong> (P-489 acceptance criterion 2), the same discipline
/// P-490's <c>ServiceDefaultsWorkflowLayeringRulesTests</c> established: during implementation, the
/// real <c>ApplicationBehaviorsBuilder.Build()</c> registration order was temporarily reverted to
/// the pre-fix defect (<c>CacheInvalidationBehavior</c> registered AFTER, not BEFORE,
/// <c>TransactionBehavior</c>) and this test was re-run — it genuinely failed (eviction observed
/// BEFORE the commit) — then the file was reverted to its correct, shipped state before commit,
/// confirmed via a clean <c>git status</c> on <c>05.Application</c>.
/// </para>
/// <para>
/// No new SK diagnostic ID and no new <c>Rules/</c>/<c>Predicates/</c> production class — mirroring
/// T-337's own precedent, this is a one-off assertion tied to one real composed pipeline, not a
/// reusable rule factory.
/// </para>
/// </remarks>
public sealed class ApplicationBehaviorsCacheInvalidationOrderingLockTests
{
    private sealed record InvalidatingCommand : ICommand, IInvalidatesCache
    {
        public IReadOnlyCollection<string> CacheKeysToInvalidate => ["governance-lock-key"];
        public IReadOnlyCollection<string> CacheTagsToInvalidate => ["governance-lock-tag"];
    }

    private sealed record AuditedInvalidatingCommand : ICommand, IInvalidatesCache, IAuditableRequest<Result>
    {
        public IReadOnlyCollection<string> CacheKeysToInvalidate => ["governance-lock-key-2"];
        public IReadOnlyCollection<string> CacheTagsToInvalidate => ["governance-lock-tag-2"];
        public string Action => "governance-lock.action";
        public string ResourceType => "GovernanceLockResource";
        public string ResourceId => "governance-lock-resource-1";
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
    /// Records the wall-clock order in which the real pipeline's <see cref="ICacheService"/>,
    /// <see cref="IUnitOfWork"/>, and <see cref="IAuditTrailWriter"/> seam members fire — the same
    /// spy shape as 05.Application's own <c>OrderRecordingSpy</c>, reimplemented independently here
    /// (this test must not source-link or otherwise depend on 05.Application's own test project,
    /// only on the real compiled production assembly).
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
            => throw new NotSupportedException("Not exercised by this ordering lock.");

        public ValueTask SetAsync<T>(string key, T value, CachePolicy policy, CancellationToken ct = default)
            => throw new NotSupportedException("Not exercised by this ordering lock.");

        public ValueTask<T> GetOrSetAsync<T>(
            string key,
            Func<CancellationToken, ValueTask<T>> factory,
            CachePolicy policy,
            CancellationToken ct = default)
            => throw new NotSupportedException("Not exercised by this ordering lock.");

        public ValueTask<IReadOnlyDictionary<string, T?>> GetManyAsync<T>(
            IEnumerable<string> keys,
            CancellationToken ct = default)
            => throw new NotSupportedException("Not exercised by this ordering lock.");

        public ValueTask SetManyAsync<T>(
            IReadOnlyDictionary<string, T> entries,
            CachePolicy policy,
            CancellationToken ct = default)
            => throw new NotSupportedException("Not exercised by this ordering lock.");
    }

    /// <summary>
    /// The default, documented registration path: only CacheInvalidation and Transaction opted in.
    /// Against the REAL, shipped <c>SharedKernel.Application.Behaviors.dll</c>, eviction must
    /// observably follow the commit.
    /// </summary>
    [Fact]
    public async Task RealApplicationBehaviorsBuilder_CacheInvalidationAndTransactionRegistered_EvictionObservablyFollowsCommit()
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

        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssemblyContaining<ApplicationBehaviorsCacheInvalidationOrderingLockTests>());
        using var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<ISender>();

        var result = await sender.Send(new InvalidatingCommand());

        result.IsSuccess.Should().BeTrue();
        spy.CallOrder.Should().Equal(
            [nameof(IUnitOfWork.SaveChangesAsync), nameof(ICacheService.RemoveAsync), nameof(ICacheService.RemoveByTagAsync)],
            "the real, compiled SharedKernel.Application.Behaviors.dll's ApplicationBehaviorsBuilder " +
            "must register CacheInvalidationBehavior CLOSER to the outer edge of the pipeline than " +
            "TransactionBehavior so eviction observably follows the commit, never precedes it " +
            "(root Phase Backlog P-489/WO-080)");
    }

    /// <summary>
    /// The full three-behavior registration path (Auditing + Transaction + CacheInvalidation),
    /// proving BOTH documented invariants hold simultaneously against the REAL assembly: Auditing's
    /// write lands inside the same commit (WO-071/P-458), and CacheInvalidation's eviction follows
    /// that same commit (WO-080/P-488) — the two invariants pull in opposite directions relative to
    /// <c>TransactionBehavior</c>'s registration position, which is exactly why a mechanical,
    /// EXECUTED lock is warranted rather than a simple "CacheInvalidation is outermost" assertion
    /// that could pass while silently breaking Auditing's inner position, or vice versa.
    /// </summary>
    [Fact]
    public async Task RealApplicationBehaviorsBuilder_AuditingTransactionAndCacheInvalidationAllRegistered_OrderIsRecordThenSaveThenEvict()
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

        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssemblyContaining<ApplicationBehaviorsCacheInvalidationOrderingLockTests>());
        using var provider = services.BuildServiceProvider();
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
            "against the real, compiled assembly, the audit write must land inside the same commit " +
            "(RecordAsync before SaveChangesAsync, WO-071/P-458), and cache eviction must only " +
            "follow a CONFIRMED commit (SaveChangesAsync before RemoveAsync/RemoveByTagAsync, " +
            "WO-080/P-488/P-489) — both invariants proven to hold simultaneously, not merely in " +
            "isolation");
    }
}
