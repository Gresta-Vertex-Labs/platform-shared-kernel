using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Presentation.SignalR.Tests.TestSupport;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Security.Abstractions;
using SharedKernel.Testing.Security;
using Xunit;

namespace SharedKernel.Presentation.SignalR.Tests.Context;

/// <summary>
/// Design D12: <c>HubCallerContext.GetTenantId()</c> and <c>GetCorrelationId()</c> on a live connection, over both
/// transports that build the connection's HTTP context differently (WebSockets keeps the request's, long polling
/// clones it). B12 regression: a tenantless connection has no tenant and so no tenant group.
/// </summary>
public sealed class HubCallerContextTests
{
    private static readonly Guid TenantId = Guid.Parse("7c9e6679-7425-40de-944b-e07fc1f90ae7");

    [Theory]
    [InlineData(HttpTransportType.WebSockets)]
    [InlineData(HttpTransportType.LongPolling)]
    public async Task GetTenantId_ReturnsTheTenantOfTheProvider(HttpTransportType transport)
    {
        await using var app = await StartAsync(tenantId: TenantId);
        await using var connection = await app.ConnectAsync(HubPaths.Context, transport: transport);

        var tenant = await connection.InvokeAsync<string?>(nameof(ContextHub.Tenant));

        tenant.Should().Be(TenantId.ToString());
    }

    [Fact]
    public async Task B12_GetTenantId_IsNull_WhenTheProviderReportsNoTenant()
    {
        await using var app = await StartAsync(tenantId: Guid.Empty);
        await using var connection = await app.ConnectAsync(HubPaths.Context);

        var tenant = await connection.InvokeAsync<string?>(nameof(ContextHub.Tenant));

        tenant.Should().BeNull();
    }

    [Fact]
    public async Task GetTenantId_IsNull_WithoutATenantProvider()
    {
        await using var app = await StartAsync(tenantId: null);
        await using var connection = await app.ConnectAsync(HubPaths.Context);

        var tenant = await connection.InvokeAsync<string?>(nameof(ContextHub.Tenant));

        tenant.Should().BeNull();
    }

    [Fact]
    public async Task B12_TenantlessConnections_AreNeverPutIntoASharedTenantGroup()
    {
        await using var app = await StartAsync(tenantId: Guid.Empty);
        await using var first = await app.ConnectAsync(HubPaths.Context);
        await using var second = await app.ConnectAsync(HubPaths.Context);

        // Before P-562 both connections stored Guid.Empty and joined the same "tenant:00000000-…" group.
        (await first.InvokeAsync<string?>(nameof(ContextHub.JoinTenantGroup))).Should().BeNull();
        (await second.InvokeAsync<string?>(nameof(ContextHub.JoinTenantGroup))).Should().BeNull();
    }

    [Fact]
    public async Task TenantConnection_JoinsItsTenantGroup()
    {
        await using var app = await StartAsync(tenantId: TenantId);
        await using var connection = await app.ConnectAsync(HubPaths.Context);

        var group = await connection.InvokeAsync<string?>(nameof(ContextHub.JoinTenantGroup));

        group.Should().Be(HubGroupNaming.TenantGroup(TenantId));
    }

    [Theory]
    [InlineData(HttpTransportType.WebSockets)]
    [InlineData(HttpTransportType.LongPolling)]
    public async Task GetCorrelationId_ReturnsTheIdTheClientSent(HttpTransportType transport)
    {
        await using var app = await StartAsync(tenantId: null);
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
        await using var app = await StartAsync(tenantId: null);
        await using var connection = await app.ConnectAsync(HubPaths.Context);

        var correlationId = await connection.InvokeAsync<string?>(nameof(ContextHub.Correlation));

        correlationId.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GetCorrelationId_ReplacesAnInvalidId()
    {
        await using var app = await StartAsync(tenantId: null);
        await using var connection = await app.ConnectAsync(
            HubPaths.Context,
            new Dictionary<string, string> { [WellKnownHeaders.CorrelationId] = "bad id <script>" });

        var correlationId = await connection.InvokeAsync<string?>(nameof(ContextHub.Correlation));

        correlationId.Should().NotBeNullOrWhiteSpace().And.NotContain("<script>");
    }

    [Fact]
    public async Task GetCorrelationId_IsNull_WithoutTheWebApiMiddleware()
    {
        await using var app = await StartAsync(tenantId: null, withWebApi: false);
        await using var connection = await app.ConnectAsync(HubPaths.Context);

        var correlationId = await connection.InvokeAsync<string?>(nameof(ContextHub.Correlation));

        correlationId.Should().BeNull();
    }

    private static Task<WebApplication> StartAsync(Guid? tenantId, bool withWebApi = true) =>
        SignalRTestHost.StartAsync(
            app => app.MapHub<ContextHub>(HubPaths.Context),
            configureBuilder: builder =>
            {
                if (tenantId is { } tenant)
                {
                    builder.Services.AddScoped<ITenantProvider>(_ => new FakeTenantProvider(tenant));
                }
            },
            withWebApi: withWebApi);
}
