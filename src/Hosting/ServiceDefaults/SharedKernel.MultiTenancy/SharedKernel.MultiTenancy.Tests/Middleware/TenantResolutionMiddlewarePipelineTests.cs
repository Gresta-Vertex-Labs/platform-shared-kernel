using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharedKernel.Execution.Context;
using SharedKernel.Execution.Tenancy;
using SharedKernel.MultiTenancy.Extensions;
using SharedKernel.MultiTenancy.Middleware;
using SharedKernel.MultiTenancy.Resolution;

namespace SharedKernel.MultiTenancy.Tests.Middleware;

/// <summary>
/// The README's registration through a real ASP.NET Core pipeline: <c>AddSharedKernelMultiTenancy()</c> registers the
/// strategies as scoped, and <c>UseMiddleware&lt;TenantResolutionMiddleware&gt;()</c> builds the middleware once from
/// the root provider. The other suites construct the middleware by hand and never saw the two disagree.
/// </summary>
public sealed class TenantResolutionMiddlewarePipelineTests
{
    [Fact]
    public async Task UseMiddleware_InDevelopment_StartsAndResolvesTheTenantFromTheHeader()
    {
        var tenant = Guid.NewGuid();
        await using var app = await StartAsync(Environments.Development);
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Id", tenant.ToString());

        string body = await client.GetStringAsync("/tenant");

        Assert.Equal(tenant.ToString("D"), body);
    }

    [Fact]
    public async Task ScopedStrategies_AreResolvedPerRequest_NeverCapturedByTheMiddleware()
    {
        await using var app = await StartAsync(Environments.Production);
        using var client = app.GetTestClient();
        var counter = app.Services.GetRequiredService<InstanceCounter>();

        // Options validation builds the strategies in scopes of its own (at startup and on first use), so measure
        // after a warm-up request: from then on each request gets exactly one new instance of a scoped strategy.
        await client.GetAsync("/tenant");
        int afterWarmUp = counter.Created;
        await client.GetAsync("/tenant");
        await client.GetAsync("/tenant");

        Assert.Equal(afterWarmUp + 2, counter.Created);
    }

    [Fact]
    public async Task WithoutADatabase_TheDefaultOrder_FailsStartup_NamingTheMissingConnectionFactory()
    {
        // The default order is Claim, Header, Database; with no IDbConnectionFactory the Database strategy cannot work.
        var exception = await Assert.ThrowsAsync<Microsoft.Extensions.Options.OptionsValidationException>(
            () => StartAsync(Environments.Production, strategyOrder: []));

        Assert.Contains("IDbConnectionFactory", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithADatabase_TheDatabaseStrategyIsTheRealOne()
    {
        await using var app = await StartAsync(
            Environments.Development,
            strategyOrder: [TenantResolutionStrategyNames.Header, TenantResolutionStrategyNames.Database],
            configure: services => services.AddSingleton(
                NSubstitute.Substitute.For<SharedKernel.Persistence.Abstractions.Connections.IDbConnectionFactory>()));

        using var scope = app.Services.CreateScope();
        Assert.Contains(
            scope.ServiceProvider.GetServices<ITenantResolutionStrategy>(),
            strategy => strategy is DatabaseTenantResolutionStrategy);
    }

    private static async Task<WebApplication> StartAsync(
        string environment,
        string[]? strategyOrder = null,
        Action<IServiceCollection>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<InstanceCounter>();
        builder.Services.AddSharedKernelMultiTenancy(o =>
            o.StrategyOrder = strategyOrder ?? ["Counting", TenantResolutionStrategyNames.Header]);
        builder.Services.AddScoped<ITenantResolutionStrategy, CountingStrategy>();
        configure?.Invoke(builder.Services);

        var app = builder.Build();
        app.UseMiddleware<TenantResolutionMiddleware>();
        app.MapGet("/tenant", () => RequestContextScope.Current?.TenantId?.Value.ToString("D") ?? "none");
        await app.StartAsync();
        return app;
    }

    private sealed class InstanceCounter
    {
        private int _created;

        public int Created => _created;

        public void Increment() => Interlocked.Increment(ref _created);
    }

    /// <summary>A scoped strategy that resolves nothing and counts how many instances the requests got.</summary>
    private sealed class CountingStrategy : ITenantResolutionStrategy
    {
        public CountingStrategy(InstanceCounter counter) => counter.Increment();

        public string StrategyName => "Counting";

        public Task<TenantId?> TryResolveAsync(HttpContext context, CancellationToken cancellationToken) =>
            Task.FromResult<TenantId?>(null);
    }
}
