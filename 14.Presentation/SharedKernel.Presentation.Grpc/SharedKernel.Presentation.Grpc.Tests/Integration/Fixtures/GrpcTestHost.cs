using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

    private const string LoopbackUrl = "http://127.0.0.1:0";

    /// <summary>
    /// Starts a host on the in-memory test server, or with <paramref name="overSockets"/> on Kestrel at a loopback port
    /// over real HTTP/2 (h2c), for what only a real connection enforces, such as a client's limit on the size of headers
    /// and trailers (call <see cref="CreateSocketClient"/> for such a host).
    /// </summary>
    public static async Task<WebApplication> StartAsync(
        string environment = Production,
        Action<SharedKernelGrpcOptions>? configureGrpc = null,
        Action<WebApplicationBuilder>? configureBuilder = null,
        Action<WebApplication>? configurePipeline = null,
        Action<GrpcServiceEndpointConventionBuilder>? configureService = null,
        IReadOnlyDictionary<string, string?>? configuration = null,
        InMemoryLoggerFactory? loggerFactory = null,
        bool useWebApi = true,
        bool overSockets = false)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.Logging.ClearProviders();

        if (overSockets)
        {
            builder.WebHost.UseUrls(LoopbackUrl);
            builder.WebHost.ConfigureKestrel(kestrel => kestrel.ConfigureEndpointDefaults(listen => listen.Protocols = HttpProtocols.Http2));
        }
        else
        {
            builder.WebHost.UseTestServer();
        }

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

    /// <summary>
    /// Creates a client that calls a host started with <c>overSockets</c> over a real HTTP/2 connection, and — as a
    /// gRPC client with that limit does — fails a call whose response headers and trailers together exceed
    /// <paramref name="maxResponseHeadersKilobytes"/>.
    /// </summary>
    public static TestService.TestServiceClient CreateSocketClient(this WebApplication app, int maxResponseHeadersKilobytes)
    {
        var address = app.Services.GetRequiredService<IServer>().Features.GetRequiredFeature<IServerAddressesFeature>().Addresses.Single();
        var channel = GrpcChannel.ForAddress(
            address,
            new GrpcChannelOptions
            {
                HttpHandler = new SocketsHttpHandler { MaxResponseHeadersLength = maxResponseHeadersKilobytes },
                DisposeHttpClient = true,
            });

        return new TestService.TestServiceClient(channel);
    }
}
