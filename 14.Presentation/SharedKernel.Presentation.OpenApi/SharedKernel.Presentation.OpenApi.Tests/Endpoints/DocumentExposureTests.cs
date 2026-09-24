using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedKernel.Presentation.OpenApi.Tests.TestSupport;
using SharedKernel.Presentation.WebApi;
using SharedKernel.Testing.Logging;
using Xunit;

namespace SharedKernel.Presentation.OpenApi.Tests.Endpoints;

/// <summary>
/// Where the documents are served, protecting them through the returned convention builder, and the startup warning
/// when they are served outside Development without protection.
/// </summary>
public sealed class DocumentExposureTests
{
    private const string DocsPermission = "docs.read";

    private const int UnprotectedDocumentsEventId = 14301;

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
        UnprotectedDocumentsWarnings(logs).Should().BeEmpty("nothing is served");
    }

    [Fact]
    public async Task ExposedOutsideDevelopment_WithoutAuthorization_WarnsOnceAtStartup()
    {
        var logs = new InMemoryLoggerFactory();
        await using var app = await StartExposedAsync(logs);

        // Logged while the host started: no request was made, so no endpoint has been built yet.
        var record = UnprotectedDocumentsWarnings(logs).Should().ContainSingle("one warning covers the documents and the reference").Subject;
        record.LogLevel.Should().Be(LogLevel.Warning);
        record.Message.Should().Contain("Production")
            .And.Contain("RequireEndpointPermission(\"docs.read\")")
            .And.Contain("AllowAnonymous()");
    }

    [Theory]
    [InlineData("RequireEndpointPermission")]
    [InlineData("RequireRole")]
    [InlineData("RequireFreshAuthentication")]
    [InlineData("RequireAuthorization")]
    [InlineData("RequireAuthorizationWithAPolicy")]
    [InlineData("AuthorizeAttribute")]
    public async Task ExposedOutsideDevelopment_WithAnAuthorizationConvention_DoesNotWarn(string convention)
    {
        var logs = new InMemoryLoggerFactory();
        await using var app = await StartExposedAsync(logs, documents => ApplyAuthorization(documents, convention));

        UnprotectedDocumentsWarnings(logs).Should().BeEmpty($"{convention} protects the documents");
    }

    [Fact]
    public async Task ExposedOutsideDevelopment_AllowAnonymous_IsADecision_AndDoesNotWarn()
    {
        var logs = new InMemoryLoggerFactory();
        await using var app = await StartExposedAsync(logs, documents => documents.AllowAnonymous());

        UnprotectedDocumentsWarnings(logs).Should().BeEmpty("AllowAnonymous() makes the documents public on purpose");
    }

    [Fact]
    public async Task ExposedOutsideDevelopment_UnderAFallbackPolicy_DoesNotWarn()
    {
        var logs = new InMemoryLoggerFactory();
        await using var app = await StartExposedAsync(
            logs,
            configureBuilder: builder => builder.Services.AddAuthorizationBuilder()
                .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build()));
        using var client = app.GetTestClient();

        UnprotectedDocumentsWarnings(logs).Should().BeEmpty("the fallback policy protects every endpoint without AllowAnonymous");
        (await client.GetAsync("/openapi/v1.json")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ExposedOutsideDevelopment_WithOnlyOtherConventions_Warns()
    {
        var logs = new InMemoryLoggerFactory();
        await using var app = await StartExposedAsync(logs, documents => documents.WithMetadata(new object()).RequireCors("docs"));

        UnprotectedDocumentsWarnings(logs).Should().ContainSingle("no convention applied requires authorization");
    }

    [Fact]
    public async Task AConventionTheProbeCannotRun_NeitherStopsTheHost_NorHidesTheOthers()
    {
        var logs = new InMemoryLoggerFactory();
        await using var app = await StartExposedAsync(logs, documents =>
        {
            // A real endpoint has a display name before its conventions run; the probe has none.
            documents.Add(endpoint => _ = endpoint.DisplayName ?? throw new InvalidOperationException("Needs a real endpoint."));
            documents.RequireEndpointPermission(DocsPermission);
        });

        UnprotectedDocumentsWarnings(logs).Should().BeEmpty("RequireEndpointPermission is still read");
    }

    [Fact]
    public async Task Development_DoesNotWarn()
    {
        var logs = new InMemoryLoggerFactory();
        await using var app = await StartExposedAsync(logs, environment: OpenApiTestHost.Development);

        UnprotectedDocumentsWarnings(logs).Should().BeEmpty("serving the documents in Development is the default");
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
    public async Task RequireEndpointPermission_OnTheReturnedBuilder_ProtectsTheDocumentsAndTheReference(string path)
    {
        await using var app = await OpenApiTestHost.StartAsync(
            OrdersApi.Map,
            configureDocuments: documents => documents.RequireEndpointPermission(DocsPermission));
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
    public async Task RequireEndpointPermission_LeavesTheReferenceScriptsPublic()
    {
        await using var app = await OpenApiTestHost.StartAsync(
            OrdersApi.Map,
            configureDocuments: documents => documents.RequireEndpointPermission(DocsPermission));
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
            configureDocuments: documents => documents.RequireEndpointPermission(DocsPermission).WithMetadata(new object()));
        using var client = app.GetTestClient();

        (await client.GetAsync("/openapi/v1.json")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static void ApplyAuthorization(IEndpointConventionBuilder documents, string convention) => _ = convention switch
    {
        "RequireEndpointPermission" => documents.RequireEndpointPermission(DocsPermission),
        "RequireRole" => documents.RequireRole("docs-reader"),
        "RequireFreshAuthentication" => documents.RequireFreshAuthentication(300),
        "RequireAuthorization" => documents.RequireAuthorization(),
        "RequireAuthorizationWithAPolicy" => documents.RequireAuthorization(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build()),
        "AuthorizeAttribute" => documents.WithMetadata(new AuthorizeAttribute()),
        _ => throw new ArgumentOutOfRangeException(nameof(convention), convention, "Unknown convention."),
    };

    private static IEnumerable<LogRecord> UnprotectedDocumentsWarnings(InMemoryLoggerFactory logs) =>
        logs.Loggers.Values.SelectMany(logger => logger.Records).Where(record => record.EventId.Id == UnprotectedDocumentsEventId);

    /// <summary>Starts a host that serves the documents outside Development, in Production by default.</summary>
    private static Task<WebApplication> StartExposedAsync(
        InMemoryLoggerFactory logs,
        Action<IEndpointConventionBuilder>? configureDocuments = null,
        Action<WebApplicationBuilder>? configureBuilder = null,
        string environment = OpenApiTestHost.Production) =>
        OpenApiTestHost.StartAsync(
            OrdersApi.Map,
            options => options.ExposeInProduction = true,
            environment: environment,
            configureBuilder: configureBuilder,
            configureDocuments: configureDocuments,
            loggerFactory: logs);
}
