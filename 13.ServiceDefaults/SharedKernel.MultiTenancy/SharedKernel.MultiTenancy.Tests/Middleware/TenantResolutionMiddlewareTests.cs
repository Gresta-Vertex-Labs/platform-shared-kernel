using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using SharedKernel.MultiTenancy.Middleware;
using SharedKernel.MultiTenancy.Resolution;

namespace SharedKernel.MultiTenancy.Tests.Middleware;

public sealed class TenantResolutionMiddlewareTests
{
    private static IOptions<TenantResolutionOptions> Options(params string[] order) =>
        Microsoft.Extensions.Options.Options.Create(new TenantResolutionOptions { StrategyOrder = order });

    [Fact]
    public async Task InvokeAsync_FirstResolvingStrategyWins_SetsTenantId()
    {
        var expectedTenantId = Guid.NewGuid();
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Tenant-Id"] = expectedTenantId.ToString();

        var claimInvoked = false;
        var claim = new RecordingStrategy(_ =>
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
            Options("Header", "Claim"));
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
            Options("Header"));
        var provider = new AmbientTenantProvider();

        await middleware.InvokeAsync(context, provider);

        Assert.Equal(Guid.Empty, provider.TenantId);
    }

    [Fact]
    public async Task InvokeAsync_ZeroStrategiesConfigured_DoesNotThrow()
    {
        var context = new DefaultHttpContext();
        RequestDelegate next = _ => Task.CompletedTask;

        var middleware = new TenantResolutionMiddleware(next, [], Options());
        var provider = new AmbientTenantProvider();

        await middleware.InvokeAsync(context, provider);

        Assert.Equal(Guid.Empty, provider.TenantId);
    }

    [Fact]
    public async Task InvokeAsync_StrategyOmittedFromOrder_IsNeverInvoked()
    {
        var context = new DefaultHttpContext();
        var databaseInvoked = false;
        var database = new RecordingStrategy(_ =>
        {
            databaseInvoked = true;
            return null;
        });

        RequestDelegate next = _ => Task.CompletedTask;

        // "Database" strategy is registered but not present in StrategyOrder.
        var middleware = new TenantResolutionMiddleware(
            next,
            [new HeaderTenantResolutionStrategy(), database],
            Options("Header"));
        var provider = new AmbientTenantProvider();

        await middleware.InvokeAsync(context, provider);

        Assert.False(databaseInvoked);
    }

    /// <summary>Minimal recording test double used for strategies named outside the
    /// Header/Claim/Database canonical set, so the middleware's name-mapping never resolves it
    /// from <see cref="TenantResolutionOptions.StrategyOrder"/> in these tests.</summary>
    private sealed class RecordingStrategy(Func<HttpContext, Guid?> resolve) : ITenantResolutionStrategy
    {
        public Task<Guid?> TryResolveAsync(HttpContext context, CancellationToken cancellationToken) =>
            Task.FromResult(resolve(context));
    }
}
