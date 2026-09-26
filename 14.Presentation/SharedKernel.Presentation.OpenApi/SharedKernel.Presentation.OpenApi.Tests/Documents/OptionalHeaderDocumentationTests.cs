using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Presentation.OpenApi.Tests.TestSupport;
using SharedKernel.Presentation.WebApi;
using Xunit;

namespace SharedKernel.Presentation.OpenApi.Tests.Documents;

/// <summary>
/// P-562 J1: the Idempotency-Key and If-Match headers an endpoint accepts without requiring them, documented as optional
/// parameters with the schema of the required form, and with the responses that refuse a header sent unusable — 400,
/// and 412 for If-Match — never 428; alike for a convention, a lambda attribute, a nullable handler parameter and an MVC
/// attribute. A requirement on the same endpoint wins; required headers are documented as before
/// (<see cref="RequiredHeaderDocumentationTests"/>).
/// </summary>
public sealed class OptionalHeaderDocumentationTests : IAsyncLifetime
{
    private const string IdempotencyKeyHeader = "Idempotency-Key";

    private const string IfMatchHeader = "If-Match";

    private WebApplication _app = null!;
    private JsonNode _document = null!;

    public async Task InitializeAsync()
    {
        _app = await OpenApiTestHost.StartAsync(OrdersApi.Map);
        using var client = _app.GetTestClient();
        _document = await client.GetDocumentAsync("v1");
    }

    public async Task DisposeAsync() => await _app.DisposeAsync();

    [Theory]
    [InlineData("/v1/orders/{id}/notes", "post")] // AcceptIdempotencyKey()
    [InlineData("/v1/orders/{id}/reminders", "post")] // IdempotencyKey? parameter
    public void AcceptedIdempotencyKey_IsAnOptionalHeader_RefusedOnlyWhenInvalid(string path, string method)
    {
        var operation = _document.Operation(path, method);
        var parameter = HeaderParameter(operation, IdempotencyKeyHeader);

        IsRequired(parameter).Should().BeFalse("a request may leave the key out");
        Json(parameter!["schema"]).Should().Be(
            Json(HeaderParameter(_document.Operation("/v1/orders", "post"), IdempotencyKeyHeader)!["schema"]),
            "a key that is sent is held to the same rule");
        parameter["description"]!.GetValue<string>().Should().Contain("Optional");

        ProblemResponseDescription(operation, "400").Should()
            .Contain(PresentationErrorCodes.IdempotencyKeyInvalid)
            .And.NotContain(PresentationErrorCodes.IdempotencyKeyRequired, "a missing key is not refused");
        operation["responses"]!["412"].Should().BeNull();
        operation["responses"]!["428"].Should().BeNull();
    }

    [Theory]
    [InlineData("put")] // AcceptIfMatch()
    [InlineData("patch")] // IfMatch<long>? parameter
    [InlineData("delete")] // [AcceptIfMatch] on the lambda
    public void AcceptedIfMatch_IsAnOptionalHeader_With400And412_Without428(string method)
    {
        var operation = _document.Operation("/v1/orders/{id}/address", method);
        var parameter = HeaderParameter(operation, IfMatchHeader);

        IsRequired(parameter).Should().BeFalse("a request without If-Match is unconditional");
        Json(parameter!["schema"]).Should().Be(Json(HeaderParameter(_document.Operation("/v1/orders/{id}", "put"), IfMatchHeader)!["schema"]));
        parameter["description"]!.GetValue<string>().Should().Contain("Optional");

        ProblemResponseDescription(operation, "400").Should()
            .Contain(PresentationErrorCodes.PreconditionInvalid)
            .And.Contain("*", "an accepted If-Match refuses * as 400");
        ProblemResponseDescription(operation, "412").Should().Contain(PresentationErrorCodes.PreconditionFailed);
        operation["responses"]!["428"].Should().BeNull("a missing If-Match is not refused");
    }

    [Theory]
    [InlineData("patch")]
    [InlineData("delete")]
    public void EveryAcceptingDeclaration_IsDocumentedExactlyLikeTheConvention(string method)
    {
        var convention = _document.Operation("/v1/orders/{id}/address", "put");
        var operation = _document.Operation("/v1/orders/{id}/address", method);

        Json(HeaderParameter(operation, IfMatchHeader)).Should().Be(Json(HeaderParameter(convention, IfMatchHeader)));

        foreach (var status in new[] { "400", "412" })
        {
            Json(operation["responses"]![status]).Should().Be(Json(convention["responses"]![status]), $"{method} documents {status} as PUT does");
        }
    }

    [Fact]
    public void AcceptingParameter_IsDocumentedExactlyLikeTheConvention()
    {
        var convention = _document.Operation("/v1/orders/{id}/notes", "post");
        var parameter = _document.Operation("/v1/orders/{id}/reminders", "post");

        Json(HeaderParameter(parameter, IdempotencyKeyHeader)).Should().Be(Json(HeaderParameter(convention, IdempotencyKeyHeader)));
        Json(parameter["responses"]!["400"]).Should().Be(Json(convention["responses"]!["400"]));
    }

    [Fact]
    public void AcceptedIfMatch_ResponsesFollowTheOperationsOwn_InStatusOrder()
    {
        _document.Operation("/v1/orders/{id}/address", "put")["responses"]!.AsObject().Select(response => response.Key)
            .Should().Equal("204", "400", "401", "403", "412", "default");
    }

    [Fact]
    public void Requirement_WinsOverAnAcceptingParameter()
    {
        var operation = _document.Operation("/v1/orders/{id}/lines", "put");

        IsRequired(HeaderParameter(operation, IfMatchHeader)).Should().BeTrue();
        ProblemResponseDescription(operation, "428").Should().Contain(PresentationErrorCodes.PreconditionRequired);
        ProblemResponseDescription(operation, "400").Should().NotContain("*", "only an accepted If-Match refuses * as 400");
    }

    [Theory]
    [InlineData("/v1/orders/{id}/reminders", "post", false)]
    [InlineData("/v1/orders/{id}/address", "patch", true)]
    public void NullableParameters_AreDocumentedAsTheirHeaderOnly(string path, string method, bool hasBody)
    {
        var operation = _document.Operation(path, method);

        operation["parameters"]!.AsArray()
            .Select(parameter => $"{parameter!["in"]!.GetValue<string>()} {parameter["name"]!.GetValue<string>()}")
            .Should().BeSubsetOf(["path id", $"header {IdempotencyKeyHeader}", $"header {IfMatchHeader}"]);

        if (hasBody)
        {
            operation["requestBody"]!["content"]!["application/json"]!["schema"]!["$ref"]!.GetValue<string>()
                .Should().Be("#/components/schemas/Order");
        }
        else
        {
            operation["requestBody"].Should().BeNull("a nullable IdempotencyKey parameter is never inferred as the body");
        }
    }

    [Fact]
    public void NullableParameters_AddTheAcceptedMetadata()
    {
        var endpoints = _app.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToArray();

        var reminders = Metadata("/orders/{id:int}/reminders", HttpMethods.Post);
        reminders.GetMetadata<IIdempotencyKeyAcceptedMetadata>().Should().NotBeNull();
        reminders.GetMetadata<IIdempotencyKeyRequiredMetadata>().Should().BeNull();

        var address = Metadata("/orders/{id:int}/address", HttpMethods.Patch);
        address.GetMetadata<IIfMatchAcceptedMetadata>().Should().NotBeNull();
        address.GetMetadata<IIfMatchRequiredMetadata>().Should().BeNull();

        EndpointMetadataCollection Metadata(string pathEnd, string method) => endpoints.Single(endpoint =>
            endpoint.RoutePattern.RawText!.EndsWith(pathEnd, StringComparison.Ordinal)
            && endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Contains(method) == true).Metadata;
    }

    [Fact]
    public async Task MvcAcceptAttributes_DocumentOptionalHeaders()
    {
        await using var app = await OpenApiTestHost.StartAsync(
            app => app.MapControllers(),
            configureBuilder: builder => builder.Services.AddControllers().AddApplicationPart(typeof(OrdersApi).Assembly));
        using var client = app.GetTestClient();
        var document = await client.GetDocumentAsync("v1");

        var note = document.Operation("/v1/mvc/orders/{id}/notes", "post");
        IsRequired(HeaderParameter(note, IdempotencyKeyHeader)).Should().BeFalse();
        ProblemResponseDescription(note, "400").Should().Contain(PresentationErrorCodes.IdempotencyKeyInvalid);
        note["responses"]!["428"].Should().BeNull();

        var patch = document.Operation("/v1/mvc/orders/{id}", "patch");
        IsRequired(HeaderParameter(patch, IfMatchHeader)).Should().BeFalse();
        ProblemResponseDescription(patch, "400").Should().Contain(PresentationErrorCodes.PreconditionInvalid);
        ProblemResponseDescription(patch, "412").Should().Contain(PresentationErrorCodes.PreconditionFailed);
        patch["responses"]!["428"].Should().BeNull();

        var update = document.Operation("/v1/mvc/orders/{id}", "put");
        IsRequired(HeaderParameter(update, IfMatchHeader)).Should().BeTrue("[RequireIfMatch] is documented as before");
        update["responses"]!["428"].Should().NotBeNull();
    }

    private static JsonNode? HeaderParameter(JsonNode operation, string name)
    {
        var parameter = operation["parameters"]?.AsArray().SingleOrDefault(candidate =>
            candidate!["in"]!.GetValue<string>() == "header"
            && string.Equals(candidate["name"]!.GetValue<string>(), name, StringComparison.OrdinalIgnoreCase));

        parameter.Should().NotBeNull($"the operation documents the {name} header");
        return parameter;
    }

    /// <summary>OpenAPI's <c>required</c> of a parameter: absent means <see langword="false"/>.</summary>
    private static bool IsRequired(JsonNode? parameter) => parameter?["required"]?.GetValue<bool>() ?? false;

    private static string ProblemResponseDescription(JsonNode operation, string status)
    {
        var response = operation["responses"]![status];

        response.Should().NotBeNull($"the operation documents {status}");
        response!["content"]!["application/problem+json"].Should().NotBeNull($"{status} is a problem response");
        return response["description"]!.GetValue<string>();
    }

    private static string Json(JsonNode? node)
    {
        node.Should().NotBeNull("both operations document it");
        return node!.ToJsonString();
    }
}
