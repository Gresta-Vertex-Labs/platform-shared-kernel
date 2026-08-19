using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using SharedKernel.Messaging.Abstractions.MessageBus;
using SharedKernel.ServiceDefaults.HealthChecks;

namespace SharedKernel.ServiceDefaults.Tests.HealthChecks;

public sealed class HealthCheckEndpointTests : IAsyncDisposable
{
    private IHost? _host;

    private async Task<IHost> StartHostAsync(
        Action<IHealthChecksBuilder>? configureChecks = null,
        bool requireAuthorization = false)
    {
        var builder = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    var healthChecksBuilder = services.AddSharedKernelHealthChecks();
                    configureChecks?.Invoke(healthChecksBuilder);
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapDefaultHealthCheckEndpoints(requireAuthorization));
                });
            });

        _host = await builder.StartAsync();
        return _host;
    }

    [Fact]
    public async Task HealthLive_ReturnsHealthy_EvenWhenFailingReadyCheckIsRegistered()
    {
        var host = await StartHostAsync(checks => checks.AddCheck(
            "failing-dependency",
            () => HealthCheckResult.Unhealthy(),
            tags: [HealthCheckTags.Ready]));

        using var client = host.GetTestClient();
        var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task HealthReady_ReturnsServiceUnavailable_WhenAReadyTaggedCheckFails()
    {
        var host = await StartHostAsync(checks => checks.AddCheck(
            "failing-dependency",
            () => HealthCheckResult.Unhealthy(),
            tags: [HealthCheckTags.Ready]));

        using var client = host.GetTestClient();
        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task HealthReady_ReturnsHealthy_WhenStartupGateIsMarkedReadyAndNoOtherChecks()
    {
        var host = await StartHostAsync();
        host.Services.GetRequiredService<SharedKernel.ServiceDefaults.Probes.StartupGate>().MarkReady();

        using var client = host.GetTestClient();
        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task HealthLive_IsUnaffectedByLiveTaggedAbsence_ReturnsHealthyWithNoLiveChecksRegistered()
    {
        // No check carries the "live" tag in this domain's base registration — /health/live
        // must still report Healthy (an empty check set is, by ASP.NET Core HealthChecks
        // convention, reported as Healthy).
        var host = await StartHostAsync();

        using var client = host.GetTestClient();
        var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task HealthReady_AggregatesReadyTaggedChecks_IndependentlyOfLiveTaggedChecks()
    {
        // A failing "live"-tagged check must never influence /health/ready's aggregation, and a
        // failing "ready"-tagged check must never influence /health/live's aggregation — the two
        // endpoints filter on disjoint tag sets and aggregate independently.
        var host = await StartHostAsync(checks =>
        {
            checks.AddCheck("failing-live-only", () => HealthCheckResult.Unhealthy(), tags: [HealthCheckTags.Live]);
            checks.AddCheck("healthy-ready-only", () => HealthCheckResult.Healthy(), tags: [HealthCheckTags.Ready]);
        });
        host.Services.GetRequiredService<SharedKernel.ServiceDefaults.Probes.StartupGate>().MarkReady();

        using var client = host.GetTestClient();
        var readyResponse = await client.GetAsync("/health/ready");
        var liveResponse = await client.GetAsync("/health/live");

        // /health/ready must be Healthy: the failing check is tagged "live" only, so it is
        // excluded from the "ready" aggregation entirely.
        Assert.Equal(HttpStatusCode.OK, readyResponse.StatusCode);
        // /health/live must report the failing "live"-tagged check, independent of the
        // healthy "ready"-tagged check's status.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, liveResponse.StatusCode);
    }

    [Fact]
    public async Task HealthLive_NeverEvaluatesMessagingReadinessChecks()
    {
        // Messaging health checks (AddMessagingReadinessCheck) are tagged "ready"+"messaging" —
        // they must never surface on /health/live, even when failing.
        var host = await StartHostAsync(checks => checks.AddCheck(
            "failing-messaging",
            () => HealthCheckResult.Unhealthy(),
            tags: [HealthCheckTags.Ready, HealthCheckTags.Messaging]));
        host.Services.GetRequiredService<SharedKernel.ServiceDefaults.Probes.StartupGate>().MarkReady();

        using var client = host.GetTestClient();
        var liveResponse = await client.GetAsync("/health/live");
        var readyResponse = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, liveResponse.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, readyResponse.StatusCode);
    }

    [Fact]
    public async Task HealthLive_NeverEvaluatesRealMessagingReadinessCheckRegistration()
    {
        // Uses the real opt-in extension method (not a synthetic AddCheck call) to prove the
        // registered messaging check is excluded from the "/health/live" predicate by tag,
        // independent of whether the underlying probe reports the bus as unhealthy.
        var probe = Substitute.For<IMessageBusProbe>();
        probe.ProbeAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new MessageBusHealth(false, "bus unreachable")));

        var host = await StartHostAsync(checks =>
        {
            checks.Services.AddSingleton(probe);
            checks.AddMessagingReadinessCheck();
        });
        host.Services.GetRequiredService<SharedKernel.ServiceDefaults.Probes.StartupGate>().MarkReady();

        using var client = host.GetTestClient();
        var liveResponse = await client.GetAsync("/health/live");

        // The messaging check is not tagged "live", so /health/live evaluates an empty set and
        // reports Healthy regardless of the (deliberately unhealthy, in this test) probe result
        // that would otherwise fail the check on /health/ready.
        Assert.Equal(HttpStatusCode.OK, liveResponse.StatusCode);
    }

    [Fact]
    public async Task MapDefaultHealthCheckEndpoints_RequireAuthorizationTrue_AttachesAuthorizeMetadataToBothEndpoints()
    {
        var host = await StartHostAsync(requireAuthorization: true);

        var dataSource = host.Services.GetRequiredService<EndpointDataSource>();
        var liveEndpoint = dataSource.Endpoints.OfType<RouteEndpoint>()
            .Single(e => e.RoutePattern.RawText == "/health/live");
        var readyEndpoint = dataSource.Endpoints.OfType<RouteEndpoint>()
            .Single(e => e.RoutePattern.RawText == "/health/ready");

        Assert.NotNull(liveEndpoint.Metadata.GetMetadata<IAuthorizeData>());
        Assert.NotNull(readyEndpoint.Metadata.GetMetadata<IAuthorizeData>());
    }

    [Fact]
    public async Task MapDefaultHealthCheckEndpoints_RequireAuthorizationDefaultsFalse_NoAuthorizeMetadata()
    {
        // Default (false) must be byte-identical to pre-P-399 behavior — no authorization metadata.
        var host = await StartHostAsync();

        var dataSource = host.Services.GetRequiredService<EndpointDataSource>();
        var liveEndpoint = dataSource.Endpoints.OfType<RouteEndpoint>()
            .Single(e => e.RoutePattern.RawText == "/health/live");
        var readyEndpoint = dataSource.Endpoints.OfType<RouteEndpoint>()
            .Single(e => e.RoutePattern.RawText == "/health/ready");

        Assert.Null(liveEndpoint.Metadata.GetMetadata<IAuthorizeData>());
        Assert.Null(readyEndpoint.Metadata.GetMetadata<IAuthorizeData>());
    }

    [Fact]
    public async Task MapDefaultHealthCheckEndpoints_RequireAuthorizationTrue_RealPipeline_UnauthenticatedRequestRejected()
    {
        // T-63's GATING acceptance criterion: proven against a real, wired authentication/
        // authorization pipeline — never merely that .RequireAuthorization() was called (the
        // metadata-level tests above already cover that weaker form).
        var host = await StartHostWithAuthenticationAsync(requireAuthorization: true);
        host.Services.GetRequiredService<SharedKernel.ServiceDefaults.Probes.StartupGate>().MarkReady();

        using var client = host.GetTestClient();
        var liveResponse = await client.GetAsync("/health/live");
        var readyResponse = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.Unauthorized, liveResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, readyResponse.StatusCode);
    }

    [Fact]
    public async Task MapDefaultHealthCheckEndpoints_RequireAuthorizationTrue_RealPipeline_AuthenticatedRequestSucceeds()
    {
        var host = await StartHostWithAuthenticationAsync(requireAuthorization: true);
        host.Services.GetRequiredService<SharedKernel.ServiceDefaults.Probes.StartupGate>().MarkReady();

        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.AuthorizedHeaderName, TestAuthHandler.AuthorizedHeaderValue);

        var liveResponse = await client.GetAsync("/health/live");
        var readyResponse = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, liveResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, readyResponse.StatusCode);
    }

    private async Task<IHost> StartHostWithAuthenticationAsync(bool requireAuthorization)
    {
        var builder = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services
                        .AddAuthentication(TestAuthHandler.SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
                    services.AddAuthorization();
                    services.AddSharedKernelHealthChecks();
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapDefaultHealthCheckEndpoints(requireAuthorization));
                });
            });

        _host = await builder.StartAsync();
        return _host;
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
    }

    /// <summary>
    /// Minimal test-only authentication handler: authenticates a request carrying the
    /// <see cref="AuthorizedHeaderName"/> header set to <see cref="AuthorizedHeaderValue"/>;
    /// every other request is left unauthenticated, letting ASP.NET Core's own authorization
    /// middleware reject it with the standard challenge (401).
    /// </summary>
    private sealed class TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "Test";
        public const string AuthorizedHeaderName = "X-Test-Auth";
        public const string AuthorizedHeaderValue = "valid";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(AuthorizedHeaderName, out var value)
                || value != AuthorizedHeaderValue)
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "test-user")], SchemeName);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, SchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
