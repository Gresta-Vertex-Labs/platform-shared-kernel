// consumer-verify — exercises the composed Program.cs shape documented in
// 13.ServiceDefaults/README.md: AddServiceDefaults() + AddSharedKernelMultiTenancy()
// resolved together end-to-end from an IHostApplicationBuilder, confirming both packages
// compose into a single, resolvable DI container with no resolution exceptions.

using System.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using SharedKernel.MultiTenancy.Extensions;
using SharedKernel.MultiTenancy.Middleware;
using SharedKernel.MultiTenancy.Resolution;
using SharedKernel.Persistence.Abstractions.Connections;
using SharedKernel.ServiceDefaults.Extensions;
using SharedKernel.ServiceDefaults.Probes;
using SharedKernel.Security.Abstractions;

// ── Surface 1: AddServiceDefaults() — mandatory first call ──────────────────
var builder = WebApplication.CreateBuilder();

builder.AddServiceDefaults();
Console.WriteLine("Surface 1 PASS: AddServiceDefaults()");

// ── Surface 2: AddSharedKernelMultiTenancy() composes alongside it ──────────
// DatabaseTenantResolutionStrategy requires an IDbConnectionFactory — any real consumer
// using the "Database" tenant resolution strategy must register one (06.Persistence
// provides concrete factories; this harness uses a no-op stand-in solely to prove the
// composed container resolves without a missing-registration exception).
builder.Services.AddSingleton<IDbConnectionFactory, NoOpDbConnectionFactory>();
builder.Services.AddSharedKernelMultiTenancy();
Console.WriteLine("Surface 2 PASS: AddSharedKernelMultiTenancy()");

var app = builder.Build();

// ── Surface 3: core ServiceDefaults services resolve without exception ──────
using (var scope = app.Services.CreateScope())
{
    var healthCheckService = scope.ServiceProvider.GetRequiredService<HealthCheckService>();
    Verify(healthCheckService is not null, "HealthCheckService resolves");

    var startupGate = scope.ServiceProvider.GetRequiredService<StartupGate>();
    Verify(!startupGate.IsReady, "StartupGate.IsReady defaults false");
}

Console.WriteLine("Surface 3 PASS: ServiceDefaults core services resolve (HealthCheckService, StartupGate)");

// ── Surface 4: MultiTenancy services resolve without exception ──────────────
using (var scope = app.Services.CreateScope())
{
    var tenantProvider = scope.ServiceProvider.GetRequiredService<ITenantProvider>();
    Verify(tenantProvider.TenantId == Guid.Empty, "AmbientTenantProvider.TenantId defaults Guid.Empty");

    var strategies = scope.ServiceProvider.GetServices<ITenantResolutionStrategy>().ToList();
    Verify(strategies.Count == 3, "All three platform ITenantResolutionStrategy implementations resolve");
    Verify(strategies.Any(s => s.StrategyName == TenantResolutionStrategyNames.Header), "Header strategy registered");
    Verify(strategies.Any(s => s.StrategyName == TenantResolutionStrategyNames.Claim), "Claim strategy registered");
    Verify(strategies.Any(s => s.StrategyName == TenantResolutionStrategyNames.Database), "Database strategy registered");
}

Console.WriteLine("Surface 4 PASS: MultiTenancy services resolve (ITenantProvider, ITenantResolutionStrategy x3)");

// ── Surface 5: TenantResolutionMiddleware is constructible from the composed container ──
using (var scope = app.Services.CreateScope())
{
    var middleware = ActivatorUtilities.CreateInstance<TenantResolutionMiddleware>(
        scope.ServiceProvider,
        (RequestDelegate)(_ => Task.CompletedTask));
    Verify(middleware is not null, "TenantResolutionMiddleware constructs from the composed container");
}

Console.WriteLine("Surface 5 PASS: TenantResolutionMiddleware constructs from the composed container");

Console.WriteLine();
Console.WriteLine("ALL SURFACES VERIFIED — consumer-verify PASSED");

static void Verify(bool condition, string label)
{
    if (!condition)
        throw new InvalidOperationException($"FAIL: {label}");
}

// ── Test double for IDbConnectionFactory ─────────────────────────────────────
/// <summary>
/// Stand-in <see cref="IDbConnectionFactory"/> used only by this verification harness so
/// <c>DatabaseTenantResolutionStrategy</c> resolves without throwing. Never opens a real
/// connection — this harness never invokes a strategy's <c>TryResolveAsync</c>, only resolves
/// the DI graph.
/// </summary>
internal sealed class NoOpDbConnectionFactory : IDbConnectionFactory
{
    public Task<IDbConnection> CreateConnectionAsync(CancellationToken ct = default)
        => throw new NotSupportedException("This harness only verifies DI resolution, not query execution.");
}
