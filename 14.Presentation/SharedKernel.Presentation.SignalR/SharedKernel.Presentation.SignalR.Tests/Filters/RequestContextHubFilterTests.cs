using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharedKernel.Execution.Context;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Presentation.SignalR.Filters;
using Xunit;

namespace SharedKernel.Presentation.SignalR.Tests.Filters;

public sealed class RequestContextHubFilterTests
{
    private static readonly TenantId Tenant = new(Guid.Parse("7b0c7f5e-2d41-4c55-9a55-0f6b1e0f3a11"));

    [Fact]
    public async Task OnConnectedAsync_RequestContextResolvable_RunsConnectInsideIt()
    {
        var requestContext = new SystemRequestContext([], "caller", Tenant);
        var lifetimeContext = CreateLifetimeContext(requestContext);
        var filter = new RequestContextHubFilter(new RequestContextAccessor());
        IRequestContext? observed = null;

        await filter.OnConnectedAsync(lifetimeContext, _ =>
        {
            observed = RequestContextScope.Current;
            return Task.CompletedTask;
        });

        observed.Should().BeSameAs(requestContext);
        RequestContextScope.Current.Should().BeNull("the scope must not leak past the connect pipeline");
    }

    [Fact]
    public async Task InvokeMethodAsync_AfterConnect_RunsTheHubMethodInsideTheConnectionsContext()
    {
        var requestContext = new SystemRequestContext([], "caller", Tenant);
        var lifetimeContext = CreateLifetimeContext(requestContext);
        var filter = new RequestContextHubFilter(new RequestContextAccessor());
        await filter.OnConnectedAsync(lifetimeContext, _ => Task.CompletedTask);

        var invocationContext = new HubInvocationContext(
            lifetimeContext.Context,
            lifetimeContext.ServiceProvider,
            lifetimeContext.Hub,
            typeof(RequestContextHubFilterTests).GetMethod(nameof(InvokeMethodAsync_AfterConnect_RunsTheHubMethodInsideTheConnectionsContext))!,
            []);
        TenantId? observedTenant = null;

        await filter.InvokeMethodAsync(invocationContext, _ =>
        {
            observedTenant = new RequestContextAccessor().Current?.TenantId;
            return ValueTask.FromResult<object?>(null);
        });

        observedTenant.Should().Be(Tenant);
    }

    [Fact]
    public async Task OnConnectedAsync_PrefersTheAmbientContext()
    {
        var ambient = new SystemRequestContext([], "ambient", Tenant);
        var lifetimeContext = CreateLifetimeContext(new SystemRequestContext([], "registered"));
        var filter = new RequestContextHubFilter(new RequestContextAccessor());
        IRequestContext? observed = null;

        using (RequestContextScope.Begin(ambient))
        {
            await filter.OnConnectedAsync(lifetimeContext, _ =>
            {
                observed = RequestContextScope.Current;
                return Task.CompletedTask;
            });
        }

        observed.Should().BeSameAs(ambient);
    }

    [Fact]
    public async Task OnConnectedAsync_NoRequestContextRegistered_DoesNotReject()
    {
        var lifetimeContext = CreateLifetimeContext(requestContext: null);
        var filter = new RequestContextHubFilter(new RequestContextAccessor());
        var nextInvoked = false;

        await filter.OnConnectedAsync(lifetimeContext, _ =>
        {
            nextInvoked = true;
            return Task.CompletedTask;
        });

        nextInvoked.Should().BeTrue();
    }

    [Fact]
    public async Task OnConnectedAsync_NoHttpContextAvailable_DoesNotReject()
    {
        var hubCallerContext = Substitute.For<HubCallerContext>();
        hubCallerContext.Items.Returns(new Dictionary<object, object?>());
        hubCallerContext.Features.Returns(new FeatureCollection());

        var lifetimeContext = new HubLifetimeContext(
            hubCallerContext,
            Substitute.For<IServiceProvider>(),
            Substitute.For<Hub>());

        var filter = new RequestContextHubFilter(new RequestContextAccessor());
        var nextInvoked = false;

        await filter.OnConnectedAsync(lifetimeContext, _ =>
        {
            nextInvoked = true;
            return Task.CompletedTask;
        });

        nextInvoked.Should().BeTrue();
    }

    private static HubLifetimeContext CreateLifetimeContext(IRequestContext? requestContext)
    {
        var services = new ServiceCollection();
        if (requestContext is not null)
            services.AddSingleton(requestContext);
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
