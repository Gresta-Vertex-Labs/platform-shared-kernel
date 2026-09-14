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

namespace SharedKernel.ServiceDefaults.Messaging.Tests.HealthChecks;

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

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
    }
}
