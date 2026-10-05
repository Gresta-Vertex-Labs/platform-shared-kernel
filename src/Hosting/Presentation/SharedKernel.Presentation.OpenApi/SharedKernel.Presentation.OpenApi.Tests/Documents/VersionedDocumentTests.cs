using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Net.Http.Headers;
using SharedKernel.Presentation.OpenApi.Tests.TestSupport;
using Xunit;

namespace SharedKernel.Presentation.OpenApi.Tests.Documents;

/// <summary>One OpenAPI document per API version, generated in-process from a real host, and the reference listing them.</summary>
public sealed class VersionedDocumentTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _app = await OpenApiTestHost.StartAsync(
            OrdersApi.Map,
            options =>
            {
                options.Title = "Orders API";
                options.Description = "Places and tracks orders.";
            });
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task EachVersion_HasItsOwnDocument_WithTheOperationsOfThatVersion()
    {
        var v1 = await _client.GetDocumentAsync("v1");
        var v2 = await _client.GetDocumentAsync("v2");

        v1["info"]!["version"]!.GetValue<string>().Should().Be("1.0");
        v2["info"]!["version"]!.GetValue<string>().Should().Be("2.0");

        var v1Paths = v1["paths"]!.AsObject().Select(path => path.Key).ToArray();
        var v2Paths = v2["paths"]!.AsObject().Select(path => path.Key).ToArray();

        v1Paths.Should().Contain(["/v1/orders", "/v1/orders/{id}", "/v1/orders/admin/stats"])
            .And.NotContain(path => path.StartsWith("/v2/", StringComparison.Ordinal))
            .And.NotContain("/v1/orders/export", "the export endpoint is mapped to version 2.0 only");
        v2Paths.Should().Contain(["/v2/orders", "/v2/orders/{id}", "/v2/orders/export"])
            .And.NotContain(path => path.StartsWith("/v1/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Documents_AreOpenApi31()
    {
        var document = await _client.GetDocumentAsync("v1");

        document["openapi"]!.GetValue<string>().Should().StartWith("3.1");
    }

    [Fact]
    public async Task Documents_AreTitledAndDescribedFromTheSettings()
    {
        foreach (var name in new[] { "v1", "v2" })
        {
            var info = (await _client.GetDocumentAsync(name))["info"]!;

            info["title"]!.GetValue<string>().Should().Be("Orders API");
            info["description"]!.GetValue<string>().Should().Be("Places and tracks orders.");
        }
    }

    [Fact]
    public async Task DocumentsEndpoints_AreNotDescribed()
    {
        var document = await _client.GetDocumentAsync("v1");

        document["paths"]!.AsObject().Select(path => path.Key)
            .Should().NotContain(path => path.Contains("openapi", StringComparison.Ordinal) || path.Contains("scalar", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnknownVersion_HasNoDocument()
    {
        using var response = await _client.GetAsync("/openapi/v3.json");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Reference_ListsOneDocumentPerVersion()
    {
        using var response = await _client.GetAsync("/scalar/");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        body.Should().Contain("<title>Orders API</title>")
            .And.Contain("openapi/v1.json")
            .And.Contain("openapi/v2.json");
    }

    [Fact]
    public async Task Reference_IsServedWithoutContentSecurityPolicy_WhileDocumentsKeepIt()
    {
        using var reference = await _client.GetAsync("/scalar/");
        using var document = await _client.GetAsync("/openapi/v1.json");

        reference.Headers.Contains(HeaderNames.ContentSecurityPolicy).Should().BeFalse("a strict policy would stop the page's scripts");
        document.Headers.GetValues(HeaderNames.ContentSecurityPolicy).Should().ContainSingle()
            .Which.Should().Contain("default-src 'none'");
    }
}
