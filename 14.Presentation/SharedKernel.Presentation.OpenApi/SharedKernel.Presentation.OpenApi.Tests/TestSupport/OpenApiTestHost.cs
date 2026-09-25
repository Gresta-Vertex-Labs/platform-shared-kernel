using System.Net;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedKernel.Presentation.WebApi;
using SharedKernel.Testing.Logging;

namespace SharedKernel.Presentation.OpenApi.Tests.TestSupport;

/// <summary>Builds real in-process hosts with the one-call setup and the OpenAPI add-on, the way a service does.</summary>
internal static class OpenApiTestHost
{
    public const string Development = "Development";

    public const string Production = "Production";

    /// <summary>
    /// Starts a <see cref="TestServer"/> host: <c>AddSharedKernelWebApi</c>, <c>AddSharedKernelOpenApi</c> (in the other
    /// order when <paramref name="openApiFirst"/>), <paramref name="configureBuilder"/>, then
    /// <c>UseSharedKernelWebApi()</c>, <paramref name="mapEndpoints"/> and <c>MapSharedKernelOpenApi()</c>, whose builder
    /// is passed to <paramref name="configureDocuments"/>.
    /// </summary>
    public static async Task<WebApplication> StartAsync(
        Action<WebApplication> mapEndpoints,
        Action<SharedKernelOpenApiOptions>? configureOpenApi = null,
        string environment = Development,
        Action<WebApplicationBuilder>? configureBuilder = null,
        Action<IEndpointConventionBuilder>? configureDocuments = null,
        IReadOnlyDictionary<string, string?>? configuration = null,
        InMemoryLoggerFactory? loggerFactory = null,
        Action<SharedKernelWebApiOptions>? configureWebApi = null,
        bool openApiFirst = false)
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

        if (openApiFirst)
        {
            builder.AddSharedKernelOpenApi(configureOpenApi);
            builder.AddSharedKernelWebApi(configureWebApi);
        }
        else
        {
            builder.AddSharedKernelWebApi(configureWebApi);
            builder.AddSharedKernelOpenApi(configureOpenApi);
        }

        builder.AddTestAuthentication();
        configureBuilder?.Invoke(builder);

        var app = builder.Build();
        app.UseSharedKernelWebApi();
        mapEndpoints(app);

        var documents = app.MapSharedKernelOpenApi();
        configureDocuments?.Invoke(documents);

        await app.StartAsync();
        return app;
    }

    /// <summary>Fetches and parses the OpenAPI document named <paramref name="documentName"/>.</summary>
    public static async Task<JsonNode> GetDocumentAsync(this HttpClient client, string documentName)
    {
        using var response = await client.GetAsync($"/openapi/{documentName}.json");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonNode.Parse(body)!;
    }

    /// <summary>Returns the operation of <paramref name="method"/> (lower case) on <paramref name="path"/>.</summary>
    public static JsonNode Operation(this JsonNode document, string path, string method)
    {
        var operation = document["paths"]?[path]?[method];
        operation.Should().NotBeNull($"the document describes {method.ToUpperInvariant()} {path}; it has: {document["paths"]?.ToJsonString()}");
        return operation!;
    }
}
