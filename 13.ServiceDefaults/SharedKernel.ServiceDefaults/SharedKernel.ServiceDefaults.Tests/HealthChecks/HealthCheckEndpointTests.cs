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

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
    }
}
