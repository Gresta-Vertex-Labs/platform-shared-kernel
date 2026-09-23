using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Logging;
using SharedKernel.Presentation.OpenApi.Tests.TestSupport;
using SharedKernel.Presentation.WebApi;
using SharedKernel.Testing.Logging;
using Xunit;

namespace SharedKernel.Presentation.OpenApi.Tests.Endpoints;

/// <summary>Where the documents are served, and protecting them through the returned convention builder.</summary>
public sealed class DocumentExposureTests
{
    private const string DocsPermission = "docs.read";

    [Fact]
    public async Task Production_ByDefault_MapsNothing_AndSaysWhy()
    {
        var logs = new InMemoryLoggerFactory();
        await using var app = await OpenApiTestHost.StartAsync(OrdersApi.Map, environment: OpenApiTestHost.Production, loggerFactory: logs);
        using var client = app.GetTestClient();

        using var document = await client.GetAsync("/openapi/v1.json");
        using var reference = await client.GetAsync("/scalar/");

        document.StatusCode.Should().Be(HttpStatusCode.NotFound);
        reference.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var record = logs.GetLogger(typeof(OpenApiEndpointExtensions).FullName!).Records.Should().ContainSingle().Subject;
        record.EventId.Id.Should().Be(14300);
        record.LogLevel.Should().Be(LogLevel.Information);
        record.Message.Should().Contain("Production");
    }

    [Fact]
    public async Task Production_WithExposeInProduction_ServesTheDocumentsAndTheReference()
    {
        await using var app = await OpenApiTestHost.StartAsync(
            OrdersApi.Map,
            options => options.ExposeInProduction = true,
            environment: OpenApiTestHost.Production);
        using var client = app.GetTestClient();

        (await client.GetAsync("/openapi/v1.json")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/openapi/v2.json")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/scalar/")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ExposeInProduction_CanComeFromConfiguration()
    {
        await using var app = await OpenApiTestHost.StartAsync(
            OrdersApi.Map,
            environment: OpenApiTestHost.Production,
            configuration: new Dictionary<string, string?> { ["SharedKernel:Presentation:OpenApi:ExposeInProduction"] = "true" });
        using var client = app.GetTestClient();

        (await client.GetAsync("/openapi/v1.json")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Development_ServesTheDocumentsByDefault()
    {
        await using var app = await OpenApiTestHost.StartAsync(OrdersApi.Map, environment: OpenApiTestHost.Development);
        using var client = app.GetTestClient();

        (await client.GetAsync("/openapi/v1.json")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/scalar/")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("/openapi/v1.json")]
    [InlineData("/scalar/")]
    public async Task RequirePermission_OnTheReturnedBuilder_ProtectsTheDocumentsAndTheReference(string path)
    {
        await using var app = await OpenApiTestHost.StartAsync(
            OrdersApi.Map,
            configureDocuments: documents => documents.RequirePermission(DocsPermission));
        using var client = app.GetTestClient();

        using var anonymous = await client.GetAsync(path);
        using var withoutPermission = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, path).SignedIn(permissions: "orders.read"));
        using var withPermission = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, path).SignedIn(permissions: DocsPermission));

        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        anonymous.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        withoutPermission.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        withPermission.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RequirePermission_LeavesTheReferenceScriptsPublic()
    {
        await using var app = await OpenApiTestHost.StartAsync(
            OrdersApi.Map,
            configureDocuments: documents => documents.RequirePermission(DocsPermission));
        using var client = app.GetTestClient();

        // The page's static script carries no API information; Scalar maps it anonymous.
        (await client.GetAsync("/scalar/scalar.js")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ConventionsOnAnUnmappedBuilder_AreIgnored()
    {
        await using var app = await OpenApiTestHost.StartAsync(
            OrdersApi.Map,
            environment: OpenApiTestHost.Production,
            configureDocuments: documents => documents.RequirePermission(DocsPermission).WithMetadata(new object()));
        using var client = app.GetTestClient();

        (await client.GetAsync("/openapi/v1.json")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
