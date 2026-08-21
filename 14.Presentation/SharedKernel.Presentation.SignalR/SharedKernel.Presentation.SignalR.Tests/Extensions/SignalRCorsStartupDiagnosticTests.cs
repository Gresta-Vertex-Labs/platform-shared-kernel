using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharedKernel.Presentation.SignalR.Extensions;
using SharedKernel.Testing.Logging;
using Xunit;

namespace SharedKernel.Presentation.SignalR.Tests.Extensions;

/// <summary>
/// Tests for <see cref="SignalRCorsStartupDiagnostic"/> (WO-063, P-418) — the type itself is
/// <c>internal</c>, so it is exercised indirectly through <see cref="SignalRExtensions.AddSharedKernelSignalR"/>'s
/// real hosted-service registration and a fully started host, mirroring
/// <c>CorsExtensionsTests</c>' established technique for another internal type in the sibling
/// <c>WebApi</c> package.
/// </summary>
public sealed class SignalRCorsStartupDiagnosticTests : IDisposable
{
    private const int MissingCorsPolicyEventId = 14102;

    private TestServer? _server;

    public void Dispose() => _server?.Dispose();

    [Fact]
    public async Task HubMappedWithNoCorsPolicy_LogsWarningOnceHostHasStarted()
    {
        var loggerFactory = CreateServer(mapHubWithCors: false);

        // The diagnostic is triggered via IHostApplicationLifetime.ApplicationStarted, which fires
        // synchronously as part of the generic host's startup sequence (TestServer construction
        // already started the host) — no extra wait is required, but a short grace period keeps
        // this robust against scheduling variance.
        await WaitForRecordAsync(loggerFactory, MissingCorsPolicyEventId);

        var logger = loggerFactory.GetLogger("SharedKernel.Presentation.SignalR.Extensions.SignalRCorsStartupDiagnostic");
        logger.Records.ShouldHaveLogged(new EventId(MissingCorsPolicyEventId), LogLevel.Warning);
    }

    [Fact]
    public async Task HubMappedWithCorsPolicyAttached_NeverLogsWarning()
    {
        var loggerFactory = CreateServer(mapHubWithCors: true);

        // Give the (non-existent, expected) warning a fair chance to appear before asserting absence.
        await Task.Delay(TimeSpan.FromMilliseconds(200));

        var logger = loggerFactory.GetLogger("SharedKernel.Presentation.SignalR.Extensions.SignalRCorsStartupDiagnostic");
        logger.Records.ShouldNotHaveLogged(new EventId(MissingCorsPolicyEventId));
    }

    private InMemoryLoggerFactory CreateServer(bool mapHubWithCors)
    {
        var loggerFactory = new InMemoryLoggerFactory();

        var hostBuilder = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddInMemoryLoggerFactory();
                    services.AddSingleton<ILoggerFactory>(loggerFactory);
                    services.AddCors();
                    services.AddSharedKernelSignalR();
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseCors();
                    app.UseEndpoints(endpoints =>
                    {
                        var hubEndpoint = endpoints.MapHub<EchoHub>("/echo");
                        if (mapHubWithCors)
                        {
                            hubEndpoint.RequireCors(policy => policy.AllowAnyOrigin());
                        }
                    });
                });
            });

        var host = hostBuilder.Start();
        _server = host.GetTestServer();
        return loggerFactory;
    }

    private static async Task WaitForRecordAsync(InMemoryLoggerFactory loggerFactory, int eventId)
    {
        var logger = loggerFactory.GetLogger("SharedKernel.Presentation.SignalR.Extensions.SignalRCorsStartupDiagnostic");
        var deadline = DateTime.UtcNow.AddSeconds(5);

        while (DateTime.UtcNow < deadline)
        {
            if (logger.Records.Any(record => record.EventId.Id == eventId))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25));
        }
    }

    private sealed class EchoHub : Hub;
}
