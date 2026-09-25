using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Presentation.OpenApi.Tests.TestSupport;
using SharedKernel.Presentation.WebApi;
using Xunit;

namespace SharedKernel.Presentation.OpenApi.Tests.Documents;

/// <summary>The default problem response of every operation, and the ProblemDetails component it references.</summary>
public sealed class ProblemResponseDocumentationTests : IAsyncLifetime
{
    private const string ProblemReference = "#/components/schemas/ProblemDetails";

    private WebApplication _app = null!;
    private JsonNode _document = null!;

    public async Task InitializeAsync()
    {
        _app = await OpenApiTestHost.StartAsync(app =>
        {
            OrdersApi.Map(app);

            // An endpoint declaring a problem response itself, for which the framework generates its own schema.
            app.NewVersionedApi("Declared")
                .MapGroup("/v{version:apiVersion}/declared")
                .HasApiVersion(1.0)
                .MapGet("/", () => "ok")
                .ProducesProblem(StatusCodes.Status404NotFound);
        });

        using var client = _app.GetTestClient();
        _document = await client.GetDocumentAsync("v1");
    }

    public async Task DisposeAsync() => await _app.DisposeAsync();

    [Fact]
    public void EveryOperation_HasADefaultProblemResponse()
    {
        var operations = _document["paths"]!.AsObject()
            .SelectMany(path => path.Value!.AsObject().Select(operation => (Name: $"{operation.Key} {path.Key}", Node: operation.Value!)))
            .ToArray();

        operations.Should().NotBeEmpty();

        foreach (var (name, operation) in operations)
        {
            var content = operation["responses"]!["default"]?["content"];
            content.Should().NotBeNull($"{name} documents a default response");
            content!.AsObject().Select(media => media.Key).Should().Equal(["application/problem+json"], name);
            content["application/problem+json"]!["schema"]!["$ref"]!.GetValue<string>().Should().Be(ProblemReference, name);
        }
    }

    [Fact]
    public void ProblemDetailsComponent_CarriesTheMembersOfEveryErrorResponse()
    {
        var schema = _document["components"]!["schemas"]!["ProblemDetails"]!;
        var properties = schema["properties"]!.AsObject();

        schema["type"]!.GetValue<string>().Should().Be("object");
        properties.Select(property => property.Key).Should().BeEquivalentTo(
        [
            "type", "title", "status", "detail", "instance",
            ProblemDetailsExtensionNames.ErrorCode,
            ProblemDetailsExtensionNames.TraceId,
            ProblemDetailsExtensionNames.CorrelationId,
            ProblemDetailsExtensionNames.Errors,
            ProblemDetailsExtensionNames.ErrorCodes,
            ProblemDetailsExtensionNames.Exception,
        ]);
        properties["status"]!["type"]!.GetValue<string>().Should().Be("integer");
        properties[ProblemDetailsExtensionNames.Errors]!["additionalProperties"]!["items"]!["type"]!.GetValue<string>().Should().Be("string");
        properties[ProblemDetailsExtensionNames.Exception]!["properties"]!.AsObject().Select(property => property.Key)
            .Should().BeEquivalentTo(["type", "message", "stackTrace"]);
        schema["required"]!.AsArray().Select(name => name!.GetValue<string>())
            .Should().BeEquivalentTo(["status", ProblemDetailsExtensionNames.ErrorCode, ProblemDetailsExtensionNames.TraceId]);
    }

    [Fact]
    public void ProblemResponsesAnEndpointDeclares_ReferenceTheSameComponent()
    {
        var notFound = _document.Operation("/v1/declared", "get")["responses"]!["404"]!;

        notFound["content"]!["application/problem+json"]!["schema"]!["$ref"]!.GetValue<string>().Should().Be(ProblemReference);
        _document["components"]!["schemas"]!["ProblemDetails"]!["properties"]![ProblemDetailsExtensionNames.ErrorCode]
            .Should().NotBeNull("the platform component replaces the one the framework generates");
    }

    [Fact]
    public void SuccessResponses_AreStillInferredFromTypedResults()
    {
        var ok = _document.Operation("/v1/orders/{id}", "get")["responses"]!["200"];

        ok.Should().NotBeNull();
        ok!["content"]!["application/json"].Should().NotBeNull();
    }

    [Fact]
    public async Task MvcActionsReturningTypedResults_AreDocumentedLikeMinimalApis()
    {
        await using var app = await OpenApiTestHost.StartAsync(
            app => app.MapControllers(),
            configureBuilder: builder => builder.Services.AddControllers().AddApplicationPart(typeof(OrdersApi).Assembly));
        using var client = app.GetTestClient();
        var responses = (await client.GetDocumentAsync("v1")).Operation("/v1/mvc/orders/{id}", "get")["responses"]!;

        responses["200"]!["content"]!["application/json"]!["schema"]!["$ref"]!.GetValue<string>()
            .Should().Be("#/components/schemas/Order", "the typed union declares the success response");
        responses["default"]!["content"]!["application/problem+json"]!["schema"]!["$ref"]!.GetValue<string>().Should().Be(ProblemReference);
    }
}
