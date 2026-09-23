using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedKernel.Presentation.Grpc.Options;
using SharedKernel.Presentation.WebApi;
using SharedKernel.Testing.Logging;

namespace SharedKernel.Presentation.Grpc.Tests.Integration.Fixtures;

/// <summary>
/// Builds real in-process gRPC hosts the way a service does: <c>AddSharedKernelWebApi</c> and
/// <c>AddSharedKernelGrpc</c>, a test authentication scheme, then <c>UseSharedKernelWebApi()</c> (correlation ids,
/// authentication, authorization) and <c>MapGrpcService</c>.
/// </summary>
internal static class GrpcTestHost
{
    public const string Production = "Production";

    public const string Development = "Development";

    public static async Task<WebApplication> StartAsync(
        string environment = Production,
        Action<SharedKernelGrpcOptions>? configureGrpc = null,
        Action<WebApplicationBuilder>? configureBuilder = null,
        Action<WebApplication>? configurePipeline = null,
        Action<GrpcServiceEndpointConventionBuilder>? configureService = null,
        IReadOnlyDictionary<string, string?>? configuration = null,
        InMemoryLoggerFactory? loggerFactory = null,
        bool useWebApi = true)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        if (configuration is not null)
        {
            builder.Configuration.AddInMemoryCollection(configuration);
        }

        if (loggerFactory is not null)
        {
            builder.Services.AddSingleton<ILoggerFactory>(loggerFactory);
        }

        if (useWebApi)
        {
            builder.AddSharedKernelWebApi();
        }

        builder.AddSharedKernelGrpc(configureGrpc);
        builder.AddTestAuthentication();
        builder.Services.AddSingleton<CallProbe>();
        configureBuilder?.Invoke(builder);

        var app = builder.Build();

        if (useWebApi)
        {
            app.UseSharedKernelWebApi();
        }
        else
        {
            // A gRPC-only service without the WebApi pipeline wires the three middlewares itself.
            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();
        }

        configurePipeline?.Invoke(app);

        var service = app.MapGrpcService<TestServiceImpl>();
        configureService?.Invoke(service);

        await app.StartAsync();
        return app;
    }

    /// <summary>Creates a client whose calls go to <paramref name="app"/>'s in-memory server.</summary>
    public static TestService.TestServiceClient CreateClient(this WebApplication app)
    {
        var server = app.GetTestServer();
        var channel = GrpcChannel.ForAddress(
            server.BaseAddress,
            new GrpcChannelOptions { HttpHandler = new ResponseVersionHandler { InnerHandler = server.CreateHandler() } });

        return new TestService.TestServiceClient(channel);
    }
}
