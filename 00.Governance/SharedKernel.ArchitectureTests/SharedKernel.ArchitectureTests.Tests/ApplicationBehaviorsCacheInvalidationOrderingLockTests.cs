using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Execution.Auditing;
using SharedKernel.Application.Behaviors.Auditing;
using SharedKernel.Application.Behaviors.CacheInvalidation;
using SharedKernel.Application.Behaviors.Caching;
using SharedKernel.Application.Behaviors.Caching.Extensions;
using SharedKernel.Application.Behaviors.Extensions;
using SharedKernel.Execution.Transactions;
using SharedKernel.Application.Messaging;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Primitives.Results;
using Xunit;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>
/// Root Phase Backlog P-489 (WO-080), updated for P-544: a genuinely EXECUTED real-assembly lock —
/// mirroring <c>SK.00.CacheEncryptionAndRedisValidationLock</c>'s T-337 "Technique A" precedent
/// (the composed-pipeline test living directly in <c>SecureDefaultsAssertionTests.cs</c>) —
/// proving, against the actual real, compiled <c>SharedKernel.Application.Behaviors</c> and
/// <c>SharedKernel.Application.Behaviors.Caching</c> assemblies'
/// <see cref="ApplicationBehaviorsBuilder"/> composition, that
/// <see cref="CacheInvalidationBehavior{TRequest,TResponse}"/>'s cache eviction executes only
/// AFTER <see cref="IUnitOfWork.SaveChangesAsync"/>'s commit.
/// </summary>
/// <remarks>
/// <para>
/// <strong>P-544 mechanism change (read before editing this file further).</strong> Prior to
/// P-544, this guarantee depended on <c>CacheInvalidationBehavior</c> being registered CLOSER to
/// the outer edge of the pipeline than <c>TransactionBehavior</c> — a fragile relative-registration-
/// order fact. As of P-544, <c>CacheInvalidationBehavior</c> no longer evicts directly from its own
/// post-<c>next()</c> code at all: it registers an <see cref="SharedKernel.Application.Behaviors.Commands.ICommandScope.OnCompleted"/>
/// callback, and <c>CommandScopeBehavior</c> — always registered outermost among the command-stage
/// behaviors by <see cref="ApplicationBehaviorsBuilder.Build"/> — runs every queued callback only
/// after the OUTERMOST command's <c>next()</c> (which includes <c>TransactionBehavior</c>'s commit)
/// has already returned. The eviction-follows-commit guarantee is therefore now structural,
/// independent of where <c>CacheInvalidationBehavior</c> itself sits in the Command-stage
/// registration order. This test still asserts the same OBSERVABLE outcome (eviction after commit)
/// against the real, compiled assemblies — it now proves the <c>OnCompleted</c> mechanism works
/// end to end, rather than a registration-order fact that no longer exists to break.
/// </para>
/// <para>
/// <strong>Why this lives in 00.Governance, not only in 05.Application's own test suite.</strong>
/// This is a deliberately INDEPENDENT, cross-domain proof on top of 05.Application's own in-domain
/// regression tests — the whole point of P-489 is that a future well-intentioned edit inside
/// <c>ApplicationBehaviorsBuilder</c>/<c>CommandScopeBehavior</c>/<c>CacheInvalidationBehavior</c>
/// must fail a build even if that domain's own tests were ever weakened or deleted. This test
/// consumes the real, compiled <c>SharedKernel.Application.Behaviors.dll</c> and
/// <c>SharedKernel.Application.Behaviors.Caching.dll</c> via test-only <c>ProjectReference</c>s
/// (<c>PrivateAssets="all"</c> — see <c>SharedKernel.ArchitectureTests.Tests.csproj</c>), never a
/// source link or a hand-rolled substitute pipeline.
/// </para>
/// <para>
/// <strong>Deliberate departure from this project's IL-only discipline</strong> — same class of
/// departure as T-337/T-336: "eviction observably follows the commit" is an EMERGENT RUNTIME
/// PROPERTY of MediatR's onion-wrapping order and the <c>ICommandScope</c> callback-queue
/// mechanism, not something a static Mono.Cecil IL walk could honestly prove. This test therefore
/// genuinely builds a real <see cref="IServiceCollection"/>, calls the real
/// <c>AddSharedKernelApplicationBehaviors().AddCachingBehaviors().AddTransactionBehavior().Build()</c>
/// chain, registers the real MediatR pipeline, and dispatches a real command through it end to end
/// via <see cref="ISender"/> — never a hand-rolled substitute pipeline.
/// </para>
/// <para>
/// No new SK diagnostic ID and no new <c>Rules/</c>/<c>Predicates/</c> production class — mirroring
/// T-337's own precedent, this is a one-off assertion tied to one real composed pipeline, not a
/// reusable rule factory.
/// </para>
/// </remarks>
public sealed class ApplicationBehaviorsCacheInvalidationOrderingLockTests
{
    /// <summary>
    /// The query whose entries the locked commands invalidate. Exists because a command names the
    /// query that owns the entry rather than a raw key string: entries are namespaced per query
    /// type, so there is no whole-key form for a command to repeat.
    /// </summary>
    private sealed record LockedQuery : ICacheableQuery<string>
    {
        public CachePolicy CachePolicy => CachePolicy.Default;
        public string CacheKey => "governance-lock-key";
        public CacheScope Scope => CacheScope.Global;
    }

    private sealed record InvalidatingCommand : ICommand, IInvalidatesCache
    {
        public IReadOnlyCollection<CacheKeyRef> CacheKeysToInvalidate => [CacheKeyRef.For<LockedQuery>("governance-lock-key")];
        public IReadOnlyCollection<string> CacheTagsToInvalidate => ["governance-lock-tag"];
        public CacheScope Scope => CacheScope.Global;
    }

    private sealed record AuditedInvalidatingCommand : ICommand, IInvalidatesCache, IAuditableRequest<Result>
    {
        public IReadOnlyCollection<CacheKeyRef> CacheKeysToInvalidate => [CacheKeyRef.For<LockedQuery>("governance-lock-key-2")];
        public IReadOnlyCollection<string> CacheTagsToInvalidate => ["governance-lock-tag-2"];
        public CacheScope Scope => CacheScope.Global;
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
        public const string Commit = "Commit";

        private readonly List<Func<CancellationToken, Task>> _beforeCommit = [];

        public List<string> CallOrder { get; } = [];

        public bool IsTransactionActive { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            CallOrder.Add(nameof(SaveChangesAsync));
            return Task.FromResult(1);
        }

        public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
            => ExecuteInTransactionAsync<object?>(async ct => { await operation(ct); return null; }, cancellationToken);

        public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, System.Data.IsolationLevel? isolationLevel, CancellationToken cancellationToken = default)
            => ExecuteInTransactionAsync(operation, cancellationToken);

        public Task<TResult> ExecuteInTransactionAsync<TResult>(Func<CancellationToken, Task<TResult>> operation, System.Data.IsolationLevel? isolationLevel, CancellationToken cancellationToken = default)
            => ExecuteInTransactionAsync(operation, cancellationToken);

        // The shared IUnitOfWork contract's order, without a database: operation, save, pre-commit
        // callbacks, commit; a failed Result commits nothing.
        public async Task<TResult> ExecuteInTransactionAsync<TResult>(Func<CancellationToken, Task<TResult>> operation, CancellationToken cancellationToken = default)
        {
            IsTransactionActive = true;
            _beforeCommit.Clear();
            try
            {
                var result = await operation(cancellationToken);
                if (result is IHasSuccessFlag { IsSuccess: false })
                    return result;

                await SaveChangesAsync(cancellationToken);
                foreach (var callback in _beforeCommit)
                    await callback(cancellationToken);

                CallOrder.Add(Commit);
                return result;
            }
            finally
            {
                IsTransactionActive = false;
            }
        }

        public void OnBeforeCommit(Func<CancellationToken, Task> callback) => _beforeCommit.Add(callback);

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

        // Every other eviction member is recorded too, so a behavior that switched to one of them
        // would still show up in the observed order rather than throwing.
        public ValueTask RemoveByTagsAsync(IEnumerable<string> tags, CancellationToken ct = default)
        {
            CallOrder.Add(nameof(RemoveByTagsAsync));
            return ValueTask.CompletedTask;
        }

        public ValueTask ExpireAsync(string key, CancellationToken ct = default)
        {
            CallOrder.Add(nameof(ExpireAsync));
            return ValueTask.CompletedTask;
        }

        public ValueTask ClearAsync(CancellationToken ct = default)
        {
            CallOrder.Add(nameof(ClearAsync));
            return ValueTask.CompletedTask;
        }

        public ValueTask<CacheLookup<T>> TryGetAsync<T>(string key, CancellationToken ct = default)
            => throw new NotSupportedException("Not exercised by this ordering lock.");

        public ValueTask SetAsync<T>(string key, T value, CachePolicy policy, CancellationToken ct = default)
            => throw new NotSupportedException("Not exercised by this ordering lock.");

        public ValueTask<T> GetOrSetAsync<T>(
            string key,
            Func<CancellationToken, ValueTask<T>> factory,
            CachePolicy policy,
            CancellationToken ct = default)
            => throw new NotSupportedException("Not exercised by this ordering lock.");

        public ValueTask<T> GetOrSetAsync<T>(
            string key,
            Func<CacheFactoryContext, CancellationToken, ValueTask<T>> factory,
            CachePolicy policy,
            CancellationToken ct = default)
            => throw new NotSupportedException("Not exercised by this ordering lock.");

        public ValueTask<IReadOnlyDictionary<string, CacheLookup<T>>> TryGetManyAsync<T>(
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
    /// The key provider the caching behaviors require, producing the real
    /// <see cref="CacheKeyFormat"/> shape under a fixed service name. The spy above only observes
    /// the eviction calls; it never builds keys.
    /// </summary>
    private sealed class LockKeyProvider : ITenantCacheKeyProvider
    {
        public string BuildKey(string entity, string id, params string[] segments)
            => CacheKeyFormat.BuildKey("governance-lock", entity, id, segments);

        public string BuildTenantKey(SharedKernel.Execution.Tenancy.TenantId tenantId, string entity, string id, params string[] segments)
            => CacheKeyFormat.BuildTenantKey("governance-lock", tenantId, entity, id, segments);
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
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<ICacheService>(spy);
        services.AddSingleton<ITenantCacheKeyProvider>(new LockKeyProvider());
        services.AddSingleton<IUnitOfWork>(spy);
        services.AddSingleton<InvalidatingCommandHandler>();
        services.AddSingleton<IRequestHandler<InvalidatingCommand, Result>>(
            sp => sp.GetRequiredService<InvalidatingCommandHandler>());

        services
            .AddSharedKernelApplicationBehaviors()
            .AddCachingBehaviors()
            .AddTransactionBehavior()
            .Build();

        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssemblyContaining<ApplicationBehaviorsCacheInvalidationOrderingLockTests>());
        using var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<ISender>();

        var result = await sender.Send(new InvalidatingCommand());

        result.IsSuccess.Should().BeTrue();
        spy.CallOrder.Should().Equal(
            [nameof(IUnitOfWork.SaveChangesAsync), OrderRecordingSpy.Commit, nameof(ICacheService.RemoveAsync), nameof(ICacheService.RemoveByTagAsync)],
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
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<ICacheService>(spy);
        services.AddSingleton<ITenantCacheKeyProvider>(new LockKeyProvider());
        services.AddSingleton<IUnitOfWork>(spy);
        services.AddSingleton<IAuditTrailWriter>(spy);
        services.AddSingleton<AuditedInvalidatingCommandHandler>();
        services.AddSingleton<IRequestHandler<AuditedInvalidatingCommand, Result>>(
            sp => sp.GetRequiredService<AuditedInvalidatingCommandHandler>());

        services
            .AddSharedKernelApplicationBehaviors()
            .AddAuditingBehavior()
            .AddTransactionBehavior()
            .AddCachingBehaviors()
            .Build();

        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssemblyContaining<ApplicationBehaviorsCacheInvalidationOrderingLockTests>());
        using var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<ISender>();

        var result = await sender.Send(new AuditedInvalidatingCommand());

        result.IsSuccess.Should().BeTrue();
        spy.CallOrder.Should().Equal(
            [
                nameof(IUnitOfWork.SaveChangesAsync),
                nameof(IAuditTrailWriter.RecordAsync),
                OrderRecordingSpy.Commit,
                nameof(ICacheService.RemoveAsync),
                nameof(ICacheService.RemoveByTagAsync)
            ],
            "against the real, compiled assembly, the audit write must land inside the same commit " +
            "(RecordAsync after the business save, before the commit, via the unit of work's " +
            "pre-commit hook — P-558), and cache eviction must only follow a CONFIRMED commit " +
            "(Commit before RemoveAsync/RemoveByTagAsync, WO-080/P-488/P-489) — both invariants " +
            "proven to hold simultaneously, not merely in isolation");
    }
}
