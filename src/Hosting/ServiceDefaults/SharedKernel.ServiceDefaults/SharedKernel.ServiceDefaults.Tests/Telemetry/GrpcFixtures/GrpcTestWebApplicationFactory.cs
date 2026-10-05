using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace SharedKernel.ServiceDefaults.Tests.Telemetry.GrpcFixtures;

/// <summary>
/// Minimal in-process ASP.NET Core host — via <see cref="WebApplicationFactory{TEntryPoint}"/>'s
/// in-memory <c>TestServer</c>, never a real Kestrel socket — hosting the test-only
/// <see cref="GreeterService"/>. Used exclusively by T-43's genuine gRPC capture tests
/// (<c>CommunicationTelemetryExtensionsTests</c>) to prove
/// <c>WithCommunicationTelemetry()</c>'s gRPC client instrumentation wiring against a real outbound
/// call, not merely that the extension method didn't throw.
/// </summary>
/// <remarks>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> normally requires a real ASP.NET Core entry
/// point type; this class supplies itself as that type marker — a standard, documented workaround
/// (see Microsoft's "Test gRPC services in ASP.NET Core" guidance) for hosting a throwaway gRPC
/// service with no production <c>Program</c> class to target.
/// </remarks>
internal sealed class GrpcTestWebApplicationFactory : WebApplicationFactory<GrpcTestWebApplicationFactory>
{
    protected override IHostBuilder? CreateHostBuilder() =>
        Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(webBuilder => webBuilder.UseStartup<Startup>());

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // WebApplicationFactory's default content-root resolution walks up from the entry-point
        // assembly's name looking for a matching project folder — a heuristic that fails here
        // because this test-only marker type has no "SharedKernel.ServiceDefaults.Tests" folder
        // directly under the repo root (it is nested under src/Hosting/ServiceDefaults/...). Pinning the
        // content root to the test binary's own output directory sidesteps that heuristic
        // entirely; this host never serves static content, so the actual value is irrelevant
        // beyond "a directory that exists".
        builder.UseContentRoot(AppContext.BaseDirectory);
        return base.CreateHost(builder);
    }

    /// <summary>Wires only what the test-only <see cref="GreeterService"/> needs — gRPC endpoint routing.</summary>
    private sealed class Startup
    {
        public void ConfigureServices(IServiceCollection services) => services.AddGrpc();

        public void Configure(IApplicationBuilder app)
        {
            app.UseRouting();
            app.UseEndpoints(endpoints => endpoints.MapGrpcService<GreeterService>());
        }
    }
}
