using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SharedKernel.MultiTenancy.Middleware;
using SharedKernel.MultiTenancy.Resolution;
using SharedKernel.Primitives.Propagation;

namespace SharedKernel.MultiTenancy.Tests.Middleware;

/// <summary>The tenant middleware tags the ambient server span with the tenant it resolved, and only that.</summary>
[Collection(NonParallelActivityCollection.Name)]
public sealed class TenantSpanTagTests : IDisposable
{
    private const string SourceName = "SharedKernel.MultiTenancy.Tests.Span";
    private static readonly ActivitySource Source = new(SourceName);

    private readonly ActivityListener _listener = new()
    {
        ShouldListenTo = source => source.Name == SourceName,
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    };

    public TenantSpanTagTests() => ActivitySource.AddActivityListener(_listener);

    public void Dispose() => _listener.Dispose();

    private static TenantResolutionMiddleware Create() => new(
        _ => Task.CompletedTask,
        Options.Create(new TenantResolutionOptions { StrategyOrder = [TenantResolutionStrategyNames.Header] }),
        NullLogger<TenantResolutionMiddleware>.Instance);

    [Fact]
    public async Task AResolvedTenant_IsTaggedOnTheAmbientSpan_ReplacingAnEarlierValue()
    {
        using var span = Source.StartActivity("server")!;
        span.SetTag(WellKnownTagKeys.TenantId, "asserted-earlier");
        var tenant = Guid.NewGuid();
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Tenant-Id"] = tenant.ToString();

        await Create().InvokeAsync(context, [new HeaderTenantResolutionStrategy()]);

        Assert.Equal(tenant.ToString(), span.GetTagItem(WellKnownTagKeys.TenantId));
    }

    [Fact]
    public async Task NoResolvedTenant_LeavesTheSpansTenantTagAsItWas()
    {
        using var span = Source.StartActivity("server")!;
        span.SetTag(WellKnownTagKeys.TenantId, "asserted-earlier");

        await Create().InvokeAsync(new DefaultHttpContext(), [new HeaderTenantResolutionStrategy()]);

        Assert.Equal("asserted-earlier", span.GetTagItem(WellKnownTagKeys.TenantId));
    }

    [Fact]
    public async Task AnInactiveTenant_IsNotTagged()
    {
        using var span = Source.StartActivity("server")!;
        var services = new ServiceCollection();
        services.AddSingleton<ITenantStatusValidator>(new Inactive());
        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        context.Request.Headers["X-Tenant-Id"] = Guid.NewGuid().ToString();

        await Create().InvokeAsync(context, [new HeaderTenantResolutionStrategy()]);

        Assert.Null(span.GetTagItem(WellKnownTagKeys.TenantId));
    }

    private sealed class Inactive : ITenantStatusValidator
    {
        public Task<bool> IsActiveAsync(TenantId tenantId, CancellationToken ct) => Task.FromResult(false);
    }
}

/// <summary>Tests that listen to an activity source and read <c>Activity.Current</c> run one at a time.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class NonParallelActivityCollection
{
    /// <summary>The collection name.</summary>
    public const string Name = "NonParallelActivity";
}
