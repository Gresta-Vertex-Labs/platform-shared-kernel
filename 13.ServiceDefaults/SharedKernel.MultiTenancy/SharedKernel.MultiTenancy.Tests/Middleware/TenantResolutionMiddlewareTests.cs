using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SharedKernel.MultiTenancy.Middleware;
using SharedKernel.MultiTenancy.Resolution;

namespace SharedKernel.MultiTenancy.Tests.Middleware;

public sealed class TenantResolutionMiddlewareTests
{
    private static IOptions<TenantResolutionOptions> Options(params string[] order) =>
        Microsoft.Extensions.Options.Options.Create(new TenantResolutionOptions { StrategyOrder = order });

    private static NullLogger<TenantResolutionMiddleware> Logger() => NullLogger<TenantResolutionMiddleware>.Instance;

    [Fact]
    public async Task InvokeAsync_FirstResolvingStrategyWins_SetsTenantId()
    {
        var expectedTenantId = Guid.NewGuid();
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Tenant-Id"] = expectedTenantId.ToString();

        var claimInvoked = false;
        var claim = new RecordingStrategy(TenantResolutionStrategyNames.Claim, _ =>
        {
            claimInvoked = true;
            return null;
        });

        var nextCalled = false;
        RequestDelegate next = _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var middleware = new TenantResolutionMiddleware(
            next,
            [new HeaderTenantResolutionStrategy(), claim],
            Options("Header", "Claim"),
            Logger());
        var provider = new AmbientTenantProvider();

        await middleware.InvokeAsync(context, provider);

        Assert.Equal(expectedTenantId, provider.TenantId);
        Assert.False(claimInvoked);
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task InvokeAsync_NoStrategyResolves_LeavesTenantIdEmpty()
    {
        var context = new DefaultHttpContext();
        RequestDelegate next = _ => Task.CompletedTask;

        var middleware = new TenantResolutionMiddleware(
            next,
            [new HeaderTenantResolutionStrategy()],
            Options("Header"),
            Logger());
        var provider = new AmbientTenantProvider();

        await middleware.InvokeAsync(context, provider);

        Assert.Equal(Guid.Empty, provider.TenantId);
    }

    [Fact]
    public async Task InvokeAsync_ZeroStrategiesConfigured_DoesNotThrow()
    {
        var context = new DefaultHttpContext();
        RequestDelegate next = _ => Task.CompletedTask;

        var middleware = new TenantResolutionMiddleware(next, [], Options(), Logger());
        var provider = new AmbientTenantProvider();

        await middleware.InvokeAsync(context, provider);

        Assert.Equal(Guid.Empty, provider.TenantId);
    }

    [Fact]
    public async Task InvokeAsync_StrategyOmittedFromOrder_IsNeverInvoked()
    {
        var context = new DefaultHttpContext();
        var databaseInvoked = false;
        var database = new RecordingStrategy(TenantResolutionStrategyNames.Database, _ =>
        {
            databaseInvoked = true;
            return null;
        });

        RequestDelegate next = _ => Task.CompletedTask;

        // "Database" strategy is registered but not present in StrategyOrder — this is a real,
        // named, registered strategy (not a structurally-unreachable test double), so the
        // assertion proves the omission logic itself, not merely that an unnamed double was
        // never reachable regardless of configuration.
        var middleware = new TenantResolutionMiddleware(
            next,
            [new HeaderTenantResolutionStrategy(), database],
            Options("Header"),
            Logger());
        var provider = new AmbientTenantProvider();

        await middleware.InvokeAsync(context, provider);

        Assert.False(databaseInvoked);
    }

    [Fact]
    public async Task InvokeAsync_CustomNonPlatformStrategy_IsInvokedWhenNamedInStrategyOrder()
    {
        // Proves the explicit-contract StrategyName mechanism (replacing type-name reflection)
        // works for a strategy whose CLR type name is not Header/Claim/Database-anything.
        var expectedTenantId = Guid.NewGuid();
        var context = new DefaultHttpContext();
        var custom = new RecordingStrategy("Gateway", _ => expectedTenantId);

        RequestDelegate next = _ => Task.CompletedTask;

        var middleware = new TenantResolutionMiddleware(
            next,
            [custom],
            Options("Gateway"),
            Logger());
        var provider = new AmbientTenantProvider();

        await middleware.InvokeAsync(context, provider);

        Assert.Equal(expectedTenantId, provider.TenantId);
    }

    [Fact]
    public async Task InvokeAsync_TenantResolves_SetsActivityBaggageToResolvedTenantId()
    {
        using var activity = new Activity("test-activity").Start();

        var expectedTenantId = Guid.NewGuid();
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Tenant-Id"] = expectedTenantId.ToString();

        RequestDelegate next = _ => Task.CompletedTask;
        var middleware = new TenantResolutionMiddleware(
            next,
            [new HeaderTenantResolutionStrategy()],
            Options("Header"),
            Logger());
        var provider = new AmbientTenantProvider();

        await middleware.InvokeAsync(context, provider);

        Assert.Equal(expectedTenantId.ToString(), Activity.Current!.GetBaggageItem(TenantBaggageKeys.TenantId));
    }

    [Fact]
    public async Task InvokeAsync_NoStrategyResolves_SetsActivityBaggageToGuidEmptySentinel()
    {
        using var activity = new Activity("test-activity").Start();

        var context = new DefaultHttpContext();
        RequestDelegate next = _ => Task.CompletedTask;

        var middleware = new TenantResolutionMiddleware(
            next,
            [new HeaderTenantResolutionStrategy()],
            Options("Header"),
            Logger());
        var provider = new AmbientTenantProvider();

        await middleware.InvokeAsync(context, provider);

        // An explicit Guid.Empty sentinel value must be set — not merely absent — so log
        // aggregation can distinguish "no tenant resolved" from "enrichment was never wired".
        Assert.Equal(Guid.Empty.ToString(), Activity.Current!.GetBaggageItem(TenantBaggageKeys.TenantId));
    }

    [Fact]
    public async Task InvokeAsync_ActivityCurrentIsNull_DoesNotThrow()
    {
        Assert.Null(Activity.Current);

        var context = new DefaultHttpContext();
        context.Request.Headers["X-Tenant-Id"] = Guid.NewGuid().ToString();
        RequestDelegate next = _ => Task.CompletedTask;

        var middleware = new TenantResolutionMiddleware(
            next,
            [new HeaderTenantResolutionStrategy()],
            Options("Header"),
            Logger());
        var provider = new AmbientTenantProvider();

        var exception = await Record.ExceptionAsync(() => middleware.InvokeAsync(context, provider));

        Assert.Null(exception);
    }

    [Fact]
    public async Task InvokeAsync_AuthenticatedClaimTenant_WinsOverConflictingHeaderTenant()
    {
        // WO-061/P-393 acceptance criterion: a request carrying both a valid authenticated JWT
        // tenant claim AND a different X-Tenant-Id header must resolve to the claim's tenant, not
        // the header's, under the corrected [Claim, Header, Database] default order.
        var claimTenantId = Guid.NewGuid();
        var headerTenantId = Guid.NewGuid();
        Assert.NotEqual(claimTenantId, headerTenantId);

        var context = new DefaultHttpContext();
        context.Request.Headers["X-Tenant-Id"] = headerTenantId.ToString();

        var claim = new RecordingStrategy(TenantResolutionStrategyNames.Claim, _ => claimTenantId);
        var header = new HeaderTenantResolutionStrategy();

        RequestDelegate next = _ => Task.CompletedTask;
        var middleware = new TenantResolutionMiddleware(
            next,
            [claim, header],
            Options(TenantResolutionStrategyNames.Claim, TenantResolutionStrategyNames.Header),
            Logger());
        var provider = new AmbientTenantProvider();

        await middleware.InvokeAsync(context, provider);

        Assert.Equal(claimTenantId, provider.TenantId);
    }

    [Fact]
    public async Task InvokeAsync_HeaderOnlyRequest_UnaffectedByClaimFirstOrder()
    {
        // Pre-existing B2B/API-key header-only path is unaffected by the P-393 reorder — Claim
        // returns null for an unauthenticated request, so Header still wins when there is no claim
        // to compete with.
        var expectedTenantId = Guid.NewGuid();
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Tenant-Id"] = expectedTenantId.ToString();

        var claim = new RecordingStrategy(TenantResolutionStrategyNames.Claim, _ => null);
        var header = new HeaderTenantResolutionStrategy();

        RequestDelegate next = _ => Task.CompletedTask;
        var middleware = new TenantResolutionMiddleware(
            next,
            [claim, header],
            Options(TenantResolutionStrategyNames.Claim, TenantResolutionStrategyNames.Header),
            Logger());
        var provider = new AmbientTenantProvider();

        await middleware.InvokeAsync(context, provider);

        Assert.Equal(expectedTenantId, provider.TenantId);
    }

    [Fact]
    public async Task InvokeAsync_NoTenantStatusValidatorRegistered_ResolvesNormally()
    {
        var expectedTenantId = Guid.NewGuid();
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().BuildServiceProvider(),
        };
        context.Request.Headers["X-Tenant-Id"] = expectedTenantId.ToString();

        RequestDelegate next = _ => Task.CompletedTask;
        var middleware = new TenantResolutionMiddleware(
            next,
            [new HeaderTenantResolutionStrategy()],
            Options("Header"),
            Logger());
        var provider = new AmbientTenantProvider();

        await middleware.InvokeAsync(context, provider);

        Assert.Equal(expectedTenantId, provider.TenantId);
    }

    [Fact]
    public async Task InvokeAsync_TenantStatusValidatorReturnsTrue_ResolvesNormally()
    {
        var expectedTenantId = Guid.NewGuid();
        var services = new ServiceCollection();
        services.AddSingleton<ITenantStatusValidator>(new StubTenantStatusValidator(isActive: true));
        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        context.Request.Headers["X-Tenant-Id"] = expectedTenantId.ToString();

        RequestDelegate next = _ => Task.CompletedTask;
        var middleware = new TenantResolutionMiddleware(
            next,
            [new HeaderTenantResolutionStrategy()],
            Options("Header"),
            Logger());
        var provider = new AmbientTenantProvider();

        await middleware.InvokeAsync(context, provider);

        Assert.Equal(expectedTenantId, provider.TenantId);
    }

    [Fact]
    public async Task InvokeAsync_TenantStatusValidatorReturnsFalse_FailsClosedToGuidEmpty()
    {
        // WO-061/P-400 acceptance criterion: a registered validator returning false for a
        // syntactically-resolved tenant ID must still result in Guid.Empty/no-tenant behavior —
        // the same fail-closed path as "no strategy resolved", never a distinct outcome.
        var services = new ServiceCollection();
        services.AddSingleton<ITenantStatusValidator>(new StubTenantStatusValidator(isActive: false));
        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        context.Request.Headers["X-Tenant-Id"] = Guid.NewGuid().ToString();

        RequestDelegate next = _ => Task.CompletedTask;
        var middleware = new TenantResolutionMiddleware(
            next,
            [new HeaderTenantResolutionStrategy()],
            Options("Header"),
            Logger());
        var provider = new AmbientTenantProvider();

        await middleware.InvokeAsync(context, provider);

        Assert.Equal(Guid.Empty, provider.TenantId);
    }

    [Fact]
    public async Task InvokeAsync_TenantStatusValidatorReturnsFalse_SetsActivityBaggageToGuidEmptySentinel()
    {
        using var activity = new Activity("test-activity").Start();

        var services = new ServiceCollection();
        services.AddSingleton<ITenantStatusValidator>(new StubTenantStatusValidator(isActive: false));
        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        context.Request.Headers["X-Tenant-Id"] = Guid.NewGuid().ToString();

        RequestDelegate next = _ => Task.CompletedTask;
        var middleware = new TenantResolutionMiddleware(
            next,
            [new HeaderTenantResolutionStrategy()],
            Options("Header"),
            Logger());
        var provider = new AmbientTenantProvider();

        await middleware.InvokeAsync(context, provider);

        Assert.Equal(Guid.Empty.ToString(), Activity.Current!.GetBaggageItem(TenantBaggageKeys.TenantId));
    }

    /// <summary>Minimal recording test double carrying an explicit, caller-supplied
    /// <see cref="StrategyName"/> so tests can exercise strategy names outside the
    /// Header/Claim/Database canonical set without any reflection-based name mapping.</summary>
    private sealed class RecordingStrategy(string strategyName, Func<HttpContext, Guid?> resolve)
        : ITenantResolutionStrategy
    {
        public string StrategyName => strategyName;

        public Task<Guid?> TryResolveAsync(HttpContext context, CancellationToken cancellationToken) =>
            Task.FromResult(resolve(context));
    }

    /// <summary>Minimal <see cref="ITenantStatusValidator"/> test double returning a fixed result.</summary>
    private sealed class StubTenantStatusValidator(bool isActive) : ITenantStatusValidator
    {
        public Task<bool> IsActiveAsync(Guid tenantId, CancellationToken ct) => Task.FromResult(isActive);
    }
}
