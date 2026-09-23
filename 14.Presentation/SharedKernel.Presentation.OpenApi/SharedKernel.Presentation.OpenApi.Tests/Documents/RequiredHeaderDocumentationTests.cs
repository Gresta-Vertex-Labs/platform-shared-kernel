using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Presentation.OpenApi.Tests.TestSupport;
using Xunit;

namespace SharedKernel.Presentation.OpenApi.Tests.Documents;

/// <summary>The Idempotency-Key and If-Match headers the WebApi core requires, documented as required parameters.</summary>
public sealed class RequiredHeaderDocumentationTests
{
    [Fact]
    public async Task RequireIdempotencyKey_DocumentsARequiredHeader()
    {
        var document = await GetDocumentAsync();
        var parameter = HeaderParameter(document.Operation("/v1/orders", "post"), "Idempotency-Key");

        parameter.Should().NotBeNull();
        parameter!["required"]!.GetValue<bool>().Should().BeTrue();
        parameter["schema"]!["type"]!.GetValue<string>().Should().Be("string");
        parameter["schema"]!["maxLength"]!.GetValue<int>().Should().Be(256);
    }

    [Fact]
    public async Task RequireIfMatch_DocumentsARequiredHeader_And412And428()
    {
        var document = await GetDocumentAsync();
        var operation = document.Operation("/v1/orders/{id}", "put");
        var parameter = HeaderParameter(operation, "If-Match");

        parameter.Should().NotBeNull();
        parameter!["required"]!.GetValue<bool>().Should().BeTrue();
        operation["responses"]!["412"]!["content"]!["application/problem+json"].Should().NotBeNull();
        operation["responses"]!["428"]!["content"]!["application/problem+json"].Should().NotBeNull();
    }

    [Fact]
    public async Task EndpointsWithoutTheRequirement_DocumentNeitherHeader()
    {
        var document = await GetDocumentAsync();

        foreach (var (path, method) in new[] { ("/v1/orders", "get"), ("/v1/orders/{id}", "get") })
        {
            var operation = document.Operation(path, method);

            HeaderParameter(operation, "Idempotency-Key").Should().BeNull($"{method} {path} does not require a key");
            HeaderParameter(operation, "If-Match").Should().BeNull($"{method} {path} does not require If-Match");
            operation["responses"]!["412"].Should().BeNull();
            operation["responses"]!["428"].Should().BeNull();
        }
    }

    [Fact]
    public async Task MvcAttributes_DocumentTheSameHeaders()
    {
        await using var app = await OpenApiTestHost.StartAsync(
            app => app.MapControllers(),
            configureBuilder: builder => builder.Services.AddControllers().AddApplicationPart(typeof(OrdersApi).Assembly));
        using var client = app.GetTestClient();
        var document = await client.GetDocumentAsync("v1");

        HeaderParameter(document.Operation("/v1/mvc/orders", "post"), "Idempotency-Key")!["required"]!.GetValue<bool>().Should().BeTrue();

        var update = document.Operation("/v1/mvc/orders/{id}", "put");
        HeaderParameter(update, "If-Match")!["required"]!.GetValue<bool>().Should().BeTrue();
        update["responses"]!["412"].Should().NotBeNull();
        update["responses"]!["428"].Should().NotBeNull();
    }

    private static JsonNode? HeaderParameter(JsonNode operation, string name) =>
        operation["parameters"]?.AsArray().SingleOrDefault(parameter =>
            parameter!["in"]!.GetValue<string>() == "header"
            && string.Equals(parameter["name"]!.GetValue<string>(), name, StringComparison.OrdinalIgnoreCase));

    private static async Task<JsonNode> GetDocumentAsync()
    {
        await using var app = await OpenApiTestHost.StartAsync(OrdersApi.Map);
        using var client = app.GetTestClient();

        return await client.GetDocumentAsync("v1");
    }
}
