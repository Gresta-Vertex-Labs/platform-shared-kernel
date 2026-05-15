// Consumer verification: confirms the dependency graph resolves and public API
// surface is accessible from a pure package reference (no project reference).

using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.Extensions;
using SharedKernel.Caching.Policies;
using SharedKernel.Caching.Redis.Abstractions;
using SharedKernel.Caching.Redis.Extensions;

// ── L1-only setup ──────────────────────────────────────────────────────────
var services = new ServiceCollection();
services.AddSharedKernelCaching();
using var l1Provider = services.BuildServiceProvider();
var cacheL1 = l1Provider.GetRequiredService<ICacheService>();
Console.WriteLine($"[L1] ICacheService resolved: {cacheL1.GetType().Name}");

// ── CachePolicy API surface ─────────────────────────────────────────────────
var defaultPolicy  = CachePolicy.Default;
var customPolicy   = CachePolicy.For(TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(5));
var taggedPolicy   = defaultPolicy.WithTags("tenant:demo", "entity:order");
var earlyRefresh   = defaultPolicy.WithEagerRefresh(0.75);
var noFailSafe     = defaultPolicy.WithFailSafeDisabled();
var noEager        = defaultPolicy.WithoutEagerRefresh();
Console.WriteLine($"[Policy] Default L1TTL={defaultPolicy.L1Duration}, L2TTL={defaultPolicy.L2Duration}");
Console.WriteLine($"[Policy] Custom tags: [{string.Join(", ", taggedPolicy.Tags)}]");
Console.WriteLine($"[Policy] EagerRefresh threshold: {earlyRefresh.EagerRefreshThreshold}");
_ = customPolicy; _ = noFailSafe; _ = noEager;

// ── L1 round-trip ──────────────────────────────────────────────────────────
var result = await cacheL1.GetOrSetAsync(
    key     : "consumer:verify:1",
    factory : _ => Task.FromResult(42),
    policy  : CachePolicy.Default,
    ct      : CancellationToken.None);
Console.WriteLine($"[L1] GetOrSetAsync result: {result}");

// ── L1 + L2 (Redis) setup ──────────────────────────────────────────────────
var services2 = new ServiceCollection();
services2.AddSharedKernelCaching()
         .AddRedisL2("localhost:6379"); // not connecting — just verifies DI wiring compiles
using var l2Provider = services2.BuildServiceProvider();
var cacheL2 = l2Provider.GetRequiredService<ICacheService>();
Console.WriteLine($"[L2] ICacheService resolved: {cacheL2.GetType().Name}");

// ── Distributed locking setup ─────────────────────────────────────────────
var services3 = new ServiceCollection();
services3.AddRedisDistributedLocking("localhost:6379"); // not connecting — DI wiring only
using var lockProvider = services3.BuildServiceProvider();
var lockService = lockProvider.GetRequiredService<IDistributedLockService>();
Console.WriteLine($"[Lock] IDistributedLockService resolved: {lockService.GetType().Name}");

Console.WriteLine("[OK] Consumer dependency graph verified.");
