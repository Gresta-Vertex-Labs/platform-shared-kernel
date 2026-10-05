using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using SharedKernel.Execution.Context;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Presentation.SignalR.Tests.TestSupport;
using SharedKernel.Primitives.Propagation;
using Xunit;

namespace SharedKernel.Presentation.SignalR.Tests.Context;

/// <summary>
/// Design D12 with P-579: <c>HubCallerContext.GetTenantId()</c> and <c>GetCorrelationId()</c>, and the ambient
/// <see cref="IRequestContext"/> a hub method runs in, on a live connection over both transports that build the
/// connection's HTTP context differently (WebSockets keeps the request's, long polling clones it). The context is the
/// one <c>UseSharedKernelRequestContext()</c> opened for the connecting request, reopened by the hub filter around every
/// invocation. B12 regression: a tenantless connection has no tenant and so no tenant group.
/// </summary>
public sealed class HubCallerContextTests
{
    private static readonly TenantId Tenant = new(Guid.Parse("7c9e6679-7425-40de-944b-e07fc1f90ae7"));

    [Theory]
    [InlineData(HttpTransportType.WebSockets)]
    [InlineData(HttpTransportType.LongPolling)]
    public async Task GetTenantId_ReturnsTheTenantOfTheCaller(HttpTransportType transport)
    {
        await using var app = await StartAsync();
        await using var connection = await app.ConnectAsync(HubPaths.Context, TestAuthentication.SignedIn(tenant: Tenant), transport);

        var tenant = await connection.InvokeAsync<string?>(nameof(ContextHub.Tenant));

        tenant.Should().Be(Tenant.ToString());
    }

    [Fact]
    public async Task B12_GetTenantId_IsNull_WhenTheCallerHasNoTenant()
    {
        await using var app = await StartAsync();
        await using var connection = await app.ConnectAsync(HubPaths.Context, TestAuthentication.SignedIn());

        var tenant = await connection.InvokeAsync<string?>(nameof(ContextHub.Tenant));

        tenant.Should().BeNull();
    }

    [Fact]
    public async Task GetTenantId_IsNull_ForAnAnonymousConnection()
    {
        await using var app = await StartAsync();
        await using var connection = await app.ConnectAsync(HubPaths.Context);

        var tenant = await connection.InvokeAsync<string?>(nameof(ContextHub.Tenant));

        tenant.Should().BeNull();
    }

    [Fact]
    public async Task B12_TenantlessConnections_AreNeverPutIntoASharedTenantGroup()
    {
        await using var app = await StartAsync();
        await using var first = await app.ConnectAsync(HubPaths.Context, TestAuthentication.SignedIn(user: "user-1"));
        await using var second = await app.ConnectAsync(HubPaths.Context, TestAuthentication.SignedIn(user: "user-2"));

        // Before P-562 both connections stored Guid.Empty and joined the same all-zero tenant group.
        (await first.InvokeAsync<string?>(nameof(ContextHub.JoinTenantGroup))).Should().BeNull();
        (await second.InvokeAsync<string?>(nameof(ContextHub.JoinTenantGroup))).Should().BeNull();
    }

    [Fact]
    public async Task TenantConnection_JoinsItsTenantGroup()
    {
        await using var app = await StartAsync();
        await using var connection = await app.ConnectAsync(HubPaths.Context, TestAuthentication.SignedIn(tenant: Tenant));

        var group = await connection.InvokeAsync<string?>(nameof(ContextHub.JoinTenantGroup));

        group.Should().Be(HubGroupNaming.TenantGroup(Tenant));
    }

    [Theory]
    [InlineData(HttpTransportType.WebSockets)]
    [InlineData(HttpTransportType.LongPolling)]
    public async Task GetCorrelationId_ReturnsTheIdTheClientSent(HttpTransportType transport)
    {
        await using var app = await StartAsync();
        await using var connection = await app.ConnectAsync(
            HubPaths.Context,
            new Dictionary<string, string> { [WellKnownHeaders.CorrelationId] = "order-flow-42" },
            transport);

        var correlationId = await connection.InvokeAsync<string?>(nameof(ContextHub.Correlation));

        correlationId.Should().Be("order-flow-42");
    }

    [Fact]
    public async Task GetCorrelationId_ReturnsTheAssignedId_WhenTheClientSentNone()
    {
        await using var app = await StartAsync();
        await using var connection = await app.ConnectAsync(HubPaths.Context);

        var correlationId = await connection.InvokeAsync<string?>(nameof(ContextHub.Correlation));

        correlationId.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GetCorrelationId_ReplacesAnInvalidId()
    {
        await using var app = await StartAsync();
        await using var connection = await app.ConnectAsync(
            HubPaths.Context,
            new Dictionary<string, string> { [WellKnownHeaders.CorrelationId] = "bad id <script>" });

        var correlationId = await connection.InvokeAsync<string?>(nameof(ContextHub.Correlation));

        correlationId.Should().NotBeNullOrWhiteSpace().And.NotContain("<script>");
    }

    [Fact]
    public async Task GetCorrelationId_DoesNotNeedTheWebApiPipeline()
    {
        await using var app = await StartAsync(withWebApi: false);
        await using var connection = await app.ConnectAsync(
            HubPaths.Context,
            new Dictionary<string, string> { [WellKnownHeaders.CorrelationId] = "order-flow-43" });

        var correlationId = await connection.InvokeAsync<string?>(nameof(ContextHub.Correlation));

        correlationId.Should().Be("order-flow-43", "UseSharedKernelRequestContext() owns the correlation id (P-579)");
    }

    [Fact]
    public async Task GetCorrelationId_IsNull_WithoutTheRequestContextMiddleware()
    {
        await using var app = await StartAsync(withRequestContext: false);
        await using var connection = await app.ConnectAsync(HubPaths.Context);

        var correlationId = await connection.InvokeAsync<string?>(nameof(ContextHub.Correlation));

        correlationId.Should().BeNull();
    }

    [Theory]
    [InlineData(HttpTransportType.WebSockets)]
    [InlineData(HttpTransportType.LongPolling)]
    public async Task P579_EveryInvocation_RunsInTheConnectionsRequestContext(HttpTransportType transport)
    {
        await using var app = await StartAsync();
        var headers = TestAuthentication.SignedIn(user: "user-9", tenant: Tenant);
        headers[WellKnownHeaders.CorrelationId] = "order-flow-44";
        await using var connection = await app.ConnectAsync(HubPaths.Context, headers, transport);

        var first = await connection.InvokeAsync<string?>(nameof(ContextHub.Caller));
        var second = await connection.InvokeAsync<string?>(nameof(ContextHub.Caller));

        first.Should().Be($"user-9|{ActorKind.User}|{Tenant}|order-flow-44");
        second.Should().Be(first, "the same connection keeps the same context");
    }

    [Fact]
    public async Task P579_AnonymousConnection_RunsInAnAnonymousContext()
    {
        await using var app = await StartAsync();
        await using var connection = await app.ConnectAsync(
            HubPaths.Context,
            new Dictionary<string, string> { [WellKnownHeaders.CorrelationId] = "order-flow-45" });

        var caller = await connection.InvokeAsync<string?>(nameof(ContextHub.Caller));

        caller.Should().Be($"|{ActorKind.Anonymous}||order-flow-45");
    }

    private static Task<WebApplication> StartAsync(bool withWebApi = true, bool withRequestContext = true) =>
        SignalRTestHost.StartAsync(
            app => app.MapHub<ContextHub>(HubPaths.Context),
            withWebApi: withWebApi,
            withRequestContext: withRequestContext);
}
