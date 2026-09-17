using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharedKernel.Presentation.SignalR.Filters;
using SharedKernel.Security.Abstractions;
using SharedKernel.Testing.Security;
using Xunit;

namespace SharedKernel.Presentation.SignalR.Tests.Filters;

public class TenantContextHubFilterTests
{
    [Fact]
    public async Task OnConnectedAsync_TenantProviderResolvable_AttachesTenantIdToItems()
    {
        var tenantId = Guid.NewGuid();
        var tenantProvider = new FakeTenantProvider(tenantId);
        var lifetimeContext = CreateLifetimeContext(tenantProvider);
        var filter = new TenantContextHubFilter();
        var nextInvoked = false;

        await filter.OnConnectedAsync(lifetimeContext, _ =>
        {
            nextInvoked = true;
            return Task.CompletedTask;
        });

        nextInvoked.Should().BeTrue();
        lifetimeContext.Context.Items[TenantContextHubFilter.ItemsKey].Should().Be(tenantId);
    }

    [Fact]
    public async Task OnConnectedAsync_NoTenantProviderRegistered_AttachesEmptyGuidAndDoesNotReject()
    {
        var lifetimeContext = CreateLifetimeContext(tenantProvider: null);
        var filter = new TenantContextHubFilter();
        var nextInvoked = false;

        await filter.OnConnectedAsync(lifetimeContext, _ =>
        {
            nextInvoked = true;
            return Task.CompletedTask;
        });

        nextInvoked.Should().BeTrue();
        lifetimeContext.Context.Items[TenantContextHubFilter.ItemsKey].Should().Be(Guid.Empty);
    }

    [Fact]
    public async Task OnConnectedAsync_NoHttpContextAvailable_AttachesEmptyGuidAndDoesNotReject()
    {
        var hubCallerContext = Substitute.For<HubCallerContext>();
        hubCallerContext.Items.Returns(new Dictionary<object, object?>());
        hubCallerContext.Features.Returns(new FeatureCollection());

        var lifetimeContext = new HubLifetimeContext(
            hubCallerContext,
            Substitute.For<IServiceProvider>(),
            Substitute.For<Hub>());

        var filter = new TenantContextHubFilter();
        var nextInvoked = false;

        await filter.OnConnectedAsync(lifetimeContext, _ =>
        {
            nextInvoked = true;
            return Task.CompletedTask;
        });

        nextInvoked.Should().BeTrue();
        lifetimeContext.Context.Items[TenantContextHubFilter.ItemsKey].Should().Be(Guid.Empty);
    }

    private static HubLifetimeContext CreateLifetimeContext(ITenantProvider? tenantProvider)
    {
        var services = new ServiceCollection();
        if (tenantProvider is not null)
            services.AddSingleton(tenantProvider);
        var serviceProvider = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = serviceProvider,
        };

        var features = new FeatureCollection();
        features.Set<IHttpContextFeature>(new TestHttpContextFeature(httpContext));

        var hubCallerContext = Substitute.For<HubCallerContext>();
        hubCallerContext.Items.Returns(new Dictionary<object, object?>());
        hubCallerContext.Features.Returns(features);

        return new HubLifetimeContext(hubCallerContext, serviceProvider, Substitute.For<Hub>());
    }

    private sealed class TestHttpContextFeature : IHttpContextFeature
    {
        public TestHttpContextFeature(HttpContext httpContext) => HttpContext = httpContext;

        public HttpContext? HttpContext { get; set; }
    }
}
