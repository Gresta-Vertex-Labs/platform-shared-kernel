using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Presentation.OpenApi.Options;
using SharedKernel.Presentation.OpenApi.Tests.TestSupport;
using SharedKernel.Presentation.WebApi;
using Xunit;

namespace SharedKernel.Presentation.OpenApi.Tests.Documents;

/// <summary>Security schemes, and the security requirement and 401/403 of protected operations only.</summary>
public sealed class SecurityDocumentationTests
{
    [Fact]
    public async Task ProtectedOperations_RequireTheBearerScheme_AndDocument401And403()
    {
        var document = await GetDocumentAsync(OrdersApi.Map);

        foreach (var (path, method) in new[]
        {
            ("/v1/orders/{id}", "get"),
            ("/v1/orders", "post"),
            ("/v1/orders/{id}", "put"),
            ("/v1/orders/admin/stats", "get"),
        })
        {
            var operation = document.Operation(path, method);

            SchemeNames(operation).Should().Equal(["Bearer"], $"{method} {path} is protected");
            operation["responses"]!["401"].Should().NotBeNull($"{method} {path} documents 401");
            operation["responses"]!["403"].Should().NotBeNull($"{method} {path} documents 403");
        }
    }

    [Theory]
    [InlineData("/v1/orders", "get")]
    [InlineData("/v1/orders/admin/ping", "get")]
    public async Task AnonymousOperations_HaveNoSecurityRequirement_And_No401Or403(string path, string method)
    {
        var document = await GetDocumentAsync(OrdersApi.Map);
        var operation = document.Operation(path, method);

        operation["security"].Should().BeNull("the endpoint is anonymous (no authorization metadata, or AllowAnonymous)");
        operation["responses"]!["401"].Should().BeNull();
        operation["responses"]!["403"].Should().BeNull();
    }

    [Fact]
    public async Task Document_DeclaresTheBearerScheme_AndNoGlobalRequirement()
    {
        var document = await GetDocumentAsync(OrdersApi.Map);
        var schemes = document["components"]!["securitySchemes"]!.AsObject();

        schemes.Select(scheme => scheme.Key).Should().Equal("Bearer");
        schemes["Bearer"]!["type"]!.GetValue<string>().Should().Be("http");
        schemes["Bearer"]!["scheme"]!.GetValue<string>().Should().Be("bearer");
        schemes["Bearer"]!["bearerFormat"]!.GetValue<string>().Should().Be("JWT");
        document["security"].Should().BeNull("only protected operations carry a requirement");
    }

    [Fact]
    public async Task ApiKeyAndMutualTls_AreDeclared_AndProtectedOperationsAcceptAnyScheme()
    {
        var document = await GetDocumentAsync(OrdersApi.Map, options =>
        {
            options.ApiKeyHeaderName = "X-Api-Key";
            options.MutualTls = true;
        });

        var schemes = document["components"]!["securitySchemes"]!;
        schemes["ApiKey"]!["type"]!.GetValue<string>().Should().Be("apiKey");
        schemes["ApiKey"]!["in"]!.GetValue<string>().Should().Be("header");
        schemes["ApiKey"]!["name"]!.GetValue<string>().Should().Be("X-Api-Key");
        schemes["MutualTls"]!["type"]!.GetValue<string>().Should().Be("mutualTLS");

        // One requirement object per scheme: satisfying any one of them is enough.
        var security = document.Operation("/v1/orders/{id}", "get")["security"]!.AsArray();
        security.Should().HaveCount(3);
        security.Select(requirement => requirement!.AsObject().Single().Key).Should().Equal("Bearer", "ApiKey", "MutualTls");
    }

    [Fact]
    public async Task WithNoSchemes_ProtectedOperationsStillDocument401And403()
    {
        var document = await GetDocumentAsync(OrdersApi.Map, options => options.Bearer = false);

        document["components"]!["securitySchemes"].Should().BeNull();

        var operation = document.Operation("/v1/orders/{id}", "get");
        operation["security"].Should().BeNull();
        operation["responses"]!["401"].Should().NotBeNull();
        operation["responses"]!["403"].Should().NotBeNull();
    }

    [Fact]
    public async Task FallbackPolicy_ProtectsEveryOperationWithoutAllowAnonymous()
    {
        var document = await GetDocumentAsync(
            OrdersApi.Map,
            configureBuilder: builder => builder.Services.AddAuthorizationBuilder()
                .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build()),
            configureDocuments: documents => documents.AllowAnonymous());

        SchemeNames(document.Operation("/v1/orders", "get")).Should().Equal("Bearer");
        document.Operation("/v1/orders/admin/ping", "get")["security"].Should().BeNull();
    }

    [Fact]
    public async Task MvcAttributes_AreDocumented()
    {
        var document = await GetDocumentAsync(
            app => app.MapControllers(),
            configureBuilder: builder => builder.Services.AddControllers().AddApplicationPart(typeof(OrdersApi).Assembly));

        SchemeNames(document.Operation("/v1/mvc/orders/{id}", "get")).Should().Equal("Bearer");
        document.Operation("/v1/mvc/orders", "get")["security"].Should().BeNull();
    }

    [Fact]
    public async Task ConventionsOnMapControllers_AreDocumented()
    {
        var document = await GetDocumentAsync(
            app => app.MapControllers().RequirePermission(OrdersApi.AdminPermission),
            configureBuilder: builder => builder.Services.AddControllers().AddApplicationPart(typeof(OrdersApi).Assembly));

        var operation = document.Operation("/v1/mvc/orders", "get");

        SchemeNames(operation).Should().Equal(["Bearer"], "the convention protects every controller action");
        operation["responses"]!["401"].Should().NotBeNull();
    }

    private static string[] SchemeNames(JsonNode operation) =>
        operation["security"]?.AsArray().SelectMany(requirement => requirement!.AsObject().Select(scheme => scheme.Key)).ToArray() ?? [];

    private static async Task<JsonNode> GetDocumentAsync(
        Action<WebApplication> mapEndpoints,
        Action<SharedKernelOpenApiOptions>? configure = null,
        Action<WebApplicationBuilder>? configureBuilder = null,
        Action<IEndpointConventionBuilder>? configureDocuments = null)
    {
        await using var app = await OpenApiTestHost.StartAsync(
            mapEndpoints,
            configure,
            configureBuilder: configureBuilder,
            configureDocuments: configureDocuments);
        using var client = app.GetTestClient();

        return await client.GetDocumentAsync("v1");
    }
}
