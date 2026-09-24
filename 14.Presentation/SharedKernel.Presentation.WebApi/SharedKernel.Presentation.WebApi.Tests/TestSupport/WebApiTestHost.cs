using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedKernel.Testing.Logging;

namespace SharedKernel.Presentation.WebApi.Tests.TestSupport;

/// <summary>Builds real in-process hosts with the one-call setup, the way a service does.</summary>
internal static class WebApiTestHost
{
    public const string Production = "Production";

    public const string Development = "Development";

    /// <summary>
    /// Starts a <see cref="TestServer"/> host: <c>AddSharedKernelWebApi</c>, then <paramref name="configureBuilder"/>,
    /// then <c>UseSharedKernelWebApi(configurePipeline)</c>, then <paramref name="mapEndpoints"/>.
    /// </summary>
    public static Task<WebApplication> StartAsync(
        Action<WebApplication> mapEndpoints,
        Action<WebApplicationBuilder>? configureBuilder = null,
        Action<SharedKernelWebApiOptions>? configureOptions = null,
        string environment = Production,
        IReadOnlyDictionary<string, string?>? configuration = null,
        InMemoryLoggerFactory? loggerFactory = null,
        Action<WebApplicationBuilder>? configureBeforeWebApi = null,
        Action<WebApiPipeline>? configurePipeline = null) =>
        StartCoreAsync(useKestrel: false, mapEndpoints, configureBuilder, configureOptions, environment, configuration, loggerFactory, configureBeforeWebApi, configurePipeline);

    /// <summary>Starts the same host on a real Kestrel listener at an ephemeral loopback port.</summary>
    public static Task<WebApplication> StartKestrelAsync(
        Action<WebApplication> mapEndpoints,
        Action<WebApplicationBuilder>? configureBuilder = null,
        Action<SharedKernelWebApiOptions>? configureOptions = null,
        string environment = Production) =>
        StartCoreAsync(useKestrel: true, mapEndpoints, configureBuilder, configureOptions, environment, configuration: null, loggerFactory: null, configureBeforeWebApi: null, configurePipeline: null);

    /// <summary>Returns the base address of a host started with <see cref="StartKestrelAsync"/>.</summary>
    public static Uri GetKestrelAddress(this WebApplication app) =>
        new(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First() + "/");

    private static async Task<WebApplication> StartCoreAsync(
        bool useKestrel,
        Action<WebApplication> mapEndpoints,
        Action<WebApplicationBuilder>? configureBuilder,
        Action<SharedKernelWebApiOptions>? configureOptions,
        string environment,
        IReadOnlyDictionary<string, string?>? configuration,
        InMemoryLoggerFactory? loggerFactory,
        Action<WebApplicationBuilder>? configureBeforeWebApi,
        Action<WebApiPipeline>? configurePipeline)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });

        if (useKestrel)
        {
            builder.WebHost.UseUrls("http://127.0.0.1:0");
        }
        else
        {
            builder.WebHost.UseTestServer();
        }

        builder.Logging.ClearProviders();

        if (configuration is not null)
        {
            builder.Configuration.AddInMemoryCollection(configuration);
        }

        if (loggerFactory is not null)
        {
            builder.Services.AddSingleton<ILoggerFactory>(loggerFactory);
        }

        // A service may register MVC before this package; IProblemDetailsService then tries MVC's writer first.
        configureBeforeWebApi?.Invoke(builder);
        builder.AddSharedKernelWebApi(configureOptions);
        configureBuilder?.Invoke(builder);

        var app = builder.Build();
        app.UseSharedKernelWebApi(configurePipeline);
        mapEndpoints(app);

        await app.StartAsync();
        return app;
    }
}
