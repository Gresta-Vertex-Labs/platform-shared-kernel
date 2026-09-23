using System.Net;
using Asp.Versioning.OpenApi.Transformers;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Presentation.OpenApi.Documents;
using SharedKernel.Presentation.OpenApi.Options;
using SharedKernel.Presentation.OpenApi.Tests.TestSupport;
using SharedKernel.Presentation.WebApi;
using Xunit;

namespace SharedKernel.Presentation.OpenApi.Tests.Setup;

/// <summary>Registration: binding, validation, defaults, idempotency and misuse.</summary>
public sealed class OpenApiSetupTests
{
    [Fact]
    public async Task Settings_AreBoundFromConfiguration()
    {
        await using var app = await OpenApiTestHost.StartAsync(
            OrdersApi.Map,
            configuration: new Dictionary<string, string?>
            {
                ["SharedKernel:Presentation:OpenApi:Title"] = "Orders",
                ["SharedKernel:Presentation:OpenApi:Description"] = "From configuration.",
                ["SharedKernel:Presentation:OpenApi:Bearer"] = "false",
                ["SharedKernel:Presentation:OpenApi:ApiKeyHeaderName"] = "X-Api-Key",
                ["SharedKernel:Presentation:OpenApi:MutualTls"] = "true",
                ["SharedKernel:Presentation:OpenApi:ExposeInProduction"] = "true",
            });

        var options = app.Services.GetRequiredService<IOptions<SharedKernelOpenApiOptions>>().Value;

        options.Title.Should().Be("Orders");
        options.Description.Should().Be("From configuration.");
        options.Bearer.Should().BeFalse();
        options.ApiKeyHeaderName.Should().Be("X-Api-Key");
        options.MutualTls.Should().BeTrue();
        options.ExposeInProduction.Should().BeTrue();
    }

    [Fact]
    public void Defaults_DocumentBearer_AndExposeNothingOutsideDevelopment()
    {
        var options = new SharedKernelOpenApiOptions();

        options.Title.Should().BeNull();
        options.Description.Should().BeNull();
        options.Versioning.Should().BeNull();
        options.Bearer.Should().BeTrue();
        options.ApiKeyHeaderName.Should().BeNull();
        options.MutualTls.Should().BeFalse();
        options.ExposeInProduction.Should().BeFalse();
        SharedKernelOpenApiOptions.SectionName.Should().Be("SharedKernel:Presentation:OpenApi");
    }

    [Fact]
    public async Task Title_DefaultsToTheApplicationName()
    {
        await using var app = await OpenApiTestHost.StartAsync(OrdersApi.Map);
        using var client = app.GetTestClient();

        var document = await client.GetDocumentAsync("v1");

        document["info"]!["title"]!.GetValue<string>().Should().Be(app.Environment.ApplicationName);
    }

    [Theory]
    [InlineData("X Api Key")]
    [InlineData("X-Api-Key:")]
    [InlineData(" ")]
    public async Task InvalidApiKeyHeaderName_StopsTheHost(string headerName)
    {
        var start = () => OpenApiTestHost.StartAsync(OrdersApi.Map, options => options.ApiKeyHeaderName = headerName);

        (await start.Should().ThrowAsync<OptionsValidationException>())
            .Which.Failures.Should().ContainSingle().Which.Should().Contain("ApiKeyHeaderName");
    }

    [Fact]
    public async Task CallingTheSetupTwice_AppliesEachCallback()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = OpenApiTestHost.Development });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.AddSharedKernelWebApi();
        builder.AddSharedKernelOpenApi(options => options.Title = "First");
        builder.AddSharedKernelOpenApi(options => options.Description = "Second");

        await using var app = builder.Build();
        app.UseSharedKernelWebApi();
        OrdersApi.Map(app);
        app.MapSharedKernelOpenApi();
        await app.StartAsync();
        using var client = app.GetTestClient();

        var info = (await client.GetDocumentAsync("v1"))["info"]!;

        info["title"]!.GetValue<string>().Should().Be("First");
        info["description"]!.GetValue<string>().Should().Be("Second");
        app.Services.GetServices<IConfigureOptions<Asp.Versioning.OpenApi.VersionedOpenApiOptions>>()
            .OfType<VersionedDocumentSetup>().Should().ContainSingle("the transformers are added once per document");
    }

    [Fact]
    public void MapSharedKernelOpenApi_WithoutTheSetup_Throws()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = OpenApiTestHost.Development });
        builder.WebHost.UseTestServer();
        var app = builder.Build();

        var map = () => app.MapSharedKernelOpenApi();

        map.Should().Throw<InvalidOperationException>().WithMessage("*AddSharedKernelOpenApi()*");
    }

    [Fact]
    public void XmlCommentsTransformer_IsTheEntryAssemblys()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = OpenApiTestHost.Development });
        builder.AddSharedKernelOpenApi();

        var registrations = builder.Services.Where(descriptor => descriptor.ServiceType == typeof(XmlCommentsTransformer)).ToArray();

        registrations.Should().ContainSingle("Asp.Versioning adds its own only when none is registered");
        registrations[0].ImplementationFactory!.Method.DeclaringType.Should().Be(typeof(EntryAssemblyXmlComments));
    }

    [Fact]
    public async Task VersionsOfEndpointsMappedAfterTheDocuments_GetDocumentsAndAreListed()
    {
        // Regression for B8: the document names were read from a second service provider built at registration, before
        // any minimal-API endpoint existed, so a version only an endpoint declares never got a document.
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = OpenApiTestHost.Development });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.AddSharedKernelWebApi();
        builder.AddSharedKernelOpenApi();

        await using var app = builder.Build();
        app.UseSharedKernelWebApi();
        app.MapSharedKernelOpenApi();
        app.NewVersionedApi("Late").MapGroup("/v{version:apiVersion}/late").HasApiVersion(3.0).MapGet("/", () => "late");
        await app.StartAsync();
        using var client = app.GetTestClient();

        var document = await client.GetDocumentAsync("v3");
        var reference = await client.GetStringAsync("/scalar/");

        document["paths"]!["/v3/late"].Should().NotBeNull();
        reference.Should().Contain("openapi/v3.json");
    }
}
