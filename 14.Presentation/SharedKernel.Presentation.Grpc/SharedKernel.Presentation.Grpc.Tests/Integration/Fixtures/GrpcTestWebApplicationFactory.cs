using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharedKernel.Presentation.Grpc.Extensions;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Security.Abstractions;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Security;

namespace SharedKernel.Presentation.Grpc.Tests.Integration.Fixtures;

/// <summary>
/// Minimal in-process ASP.NET Core host — via <see cref="WebApplicationFactory{TEntryPoint}"/>'s
/// in-memory <c>TestServer</c>, never a real Kestrel socket — hosting the test-only
/// <see cref="TestServiceImpl"/> behind <c>AddSharedKernelGrpc()</c>. Mirrors
/// <c>SharedKernel.ServiceDefaults.Tests</c>'s <c>GrpcTestWebApplicationFactory</c> (T-43/WO-056)
/// exactly, extended with default <see cref="IUserContext"/>/<see cref="ITenantProvider"/>/
/// <see cref="IClock"/> fakes each test can override per-scenario via
/// <see cref="WebApplicationFactory{TEntryPoint}.WithWebHostBuilder"/> +
/// <c>ConfigureTestServices</c> (last registration wins for single-instance DI resolution).
/// </summary>
internal sealed class GrpcTestWebApplicationFactory : WebApplicationFactory<GrpcTestWebApplicationFactory>
{
    protected override IHostBuilder? CreateHostBuilder() =>
        Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(webBuilder => webBuilder
                // Overriding CreateHostBuilder bypasses WebApplicationFactory's own default
                // environment wiring, so the ambient ASPNETCORE_ENVIRONMENT/DOTNET_ENVIRONMENT
                // (absent in a CI/test shell) would otherwise leave this host at the BCL's
                // "Production" default. Pin Development explicitly so the exception-detail
                // exposure gate (IHostEnvironment.IsDevelopment()) has a known, deliberate
                // starting point; individual tests override via WithWebHostBuilder(...).UseEnvironment(...).
                .UseEnvironment(Environments.Development)
                .UseStartup<Startup>());

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // See SharedKernel.ServiceDefaults.Tests' identical fixture: WebApplicationFactory's
        // default content-root heuristic fails for a test-only marker type nested under a
        // non-root-adjacent folder. The actual value is irrelevant beyond "a directory that exists".
        builder.UseContentRoot(AppContext.BaseDirectory);
        return base.CreateHost(builder);
    }

    /// <summary>Wires only what the test-only <see cref="TestServiceImpl"/> needs — gRPC endpoint routing.</summary>
    private sealed class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSharedKernelGrpc();

            // Defaults — authenticated user with no roles/permissions/AMR, a non-empty tenant, a
            // deterministic fixed clock. Individual tests override any of these via
            // WithWebHostBuilder(...).ConfigureTestServices(...) — the last registration wins.
            services.AddSingleton<IUserContext>(new FakeUserContext());
            services.AddSingleton<ITenantProvider>(new FakeTenantProvider());
            services.AddSingleton<IClock>(new FakeClock());
        }

        public void Configure(IApplicationBuilder app)
        {
            app.UseRouting();
            app.UseEndpoints(endpoints => endpoints.MapGrpcService<TestServiceImpl>());
        }
    }
}
