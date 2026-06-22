using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using SharedKernel.ServiceDefaults.HealthChecks;

namespace SharedKernel.ServiceDefaults.Tests.HealthChecks;

public sealed class HealthCheckEndpointTests : IAsyncDisposable
{
    private IHost? _host;

    private async Task<IHost> StartHostAsync(Action<IHealthChecksBuilder>? configureChecks = null)
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
                    app.UseEndpoints(endpoints => endpoints.MapDefaultHealthCheckEndpoints());
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
        // Messaging health checks (AddRabbitMqMessagingHealthCheck, AddAzureServiceBusMessagingHealthCheck)
        // are tagged "ready"+"messaging" — they must never surface on /health/live, even when failing.
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
    public async Task HealthLive_NeverEvaluatesRealRabbitMqOrAzureServiceBusHealthCheckRegistrations()
    {
        // Uses the real opt-in extension methods (not synthetic AddCheck calls) to prove the
        // registered RabbitMQ/ASB checks are excluded from the "/health/live" predicate by tag,
        // independent of whether the underlying broker connection actually succeeds or fails.
        var host = await StartHostAsync(checks => checks
            .AddRabbitMqMessagingHealthCheck("amqp://localhost")
            .AddAzureServiceBusMessagingHealthCheck("my-namespace.servicebus.windows.net"));
        host.Services.GetRequiredService<SharedKernel.ServiceDefaults.Probes.StartupGate>().MarkReady();

        using var client = host.GetTestClient();
        var liveResponse = await client.GetAsync("/health/live");

        // Neither messaging check is tagged "live", so /health/live evaluates an empty set and
        // reports Healthy regardless of whether the (unreachable, in this test) broker would
        // otherwise fail the check on /health/ready.
        Assert.Equal(HttpStatusCode.OK, liveResponse.StatusCode);
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
    }
}
