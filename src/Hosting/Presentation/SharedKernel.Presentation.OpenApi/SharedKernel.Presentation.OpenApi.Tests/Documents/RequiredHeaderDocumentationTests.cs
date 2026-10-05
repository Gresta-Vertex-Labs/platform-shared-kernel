using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Presentation.OpenApi.Tests.TestSupport;
using SharedKernel.Presentation.WebApi;
using SharedKernel.Primitives.Propagation;
using Xunit;

namespace SharedKernel.Presentation.OpenApi.Tests.Documents;

/// <summary>
/// The Idempotency-Key and If-Match headers the WebApi core requires, documented as required parameters with the
/// responses that refuse a request without them — alike for a convention, an attribute and a handler parameter — and
/// the Idempotency-Key schema admitting exactly the values the core accepts.
/// </summary>
public sealed class RequiredHeaderDocumentationTests
{
    private const string IdempotencyKeyHeader = "Idempotency-Key";

    private const string IfMatchHeader = "If-Match";

    /// <summary>Header values at the edges of the Idempotency-Key rule, with whether the server accepts each.</summary>
    private static readonly (string Value, bool Accepted)[] IdempotencyKeyHeaderValues =
    [
        ("k", true),
        ("pay-7f3a", true),
        ("\"pay-7f3a\"", true),
        (new string('k', IdempotencyKey.MaxLength), true),
        ($"\"{new string('k', IdempotencyKey.MaxLength)}\"", true),
        ("\"", true), // a one-character key
        ("\"k", true), // a quote that encloses nothing is part of the key
        ("k\"", true),
        ("\"k\"k\"", true), // one pair of quotes removed: k"k
        ("a,b;c=d", true),
        (new string('k', IdempotencyKey.MaxLength + 1), false),
        ($"\"{new string('k', IdempotencyKey.MaxLength + 1)}\"", false),
        ($"\"{new string('k', IdempotencyKey.MaxLength)}", false), // not enclosed, so one character too long
        ("\"\"", false), // encloses no key
        (string.Empty, false),
        ("has space", false),
        ("\" k\"", false),
        ("tab\tkey", false),
        ("anahtar-ş", false),
    ];

    [Fact]
    public async Task RequireIdempotencyKey_DocumentsARequiredHeader_And400()
    {
        var document = await GetDocumentAsync();
        var operation = document.Operation("/v1/orders", "post");
        var parameter = HeaderParameter(operation, IdempotencyKeyHeader);

        parameter.Should().NotBeNull();
        parameter!["required"]!.GetValue<bool>().Should().BeTrue();
        parameter["schema"]!["type"]!.GetValue<string>().Should().Be("string");
        parameter["schema"]!["pattern"].Should().NotBeNull("the pattern states the characters and the length of a key");
        parameter["schema"]!["maxLength"].Should().BeNull("a quoted key is longer than the key itself; the pattern bounds both forms");

        ProblemResponseDescription(operation, "400").Should()
            .Contain(PresentationErrorCodes.IdempotencyKeyRequired)
            .And.Contain(PresentationErrorCodes.IdempotencyKeyInvalid)
            .And.NotContain(PresentationErrorCodes.PreconditionInvalid);
        operation["responses"]!["412"].Should().BeNull();
        operation["responses"]!["428"].Should().BeNull();
    }

    [Fact]
    public async Task IdempotencyKeySchema_AdmitsExactlyTheHeaderValuesTheServerAccepts()
    {
        // A client that validates the header against the document must send every key the server takes and no other:
        // a quoted key of IdempotencyKey.MaxLength characters is two characters longer than that.
        var schema = HeaderParameter((await GetDocumentAsync()).Operation("/v1/orders", "post"), IdempotencyKeyHeader)!["schema"]!;
        var pattern = new Regex(schema["pattern"]!.GetValue<string>(), RegexOptions.ECMAScript);

        foreach (var (value, accepted) in IdempotencyKeyHeaderValues)
        {
            var request = new DefaultHttpContext();
            request.Request.Headers[WellKnownHeaders.IdempotencyKey] = value;
            var what = $"\"{value}\" ({value.Length} characters)";

            (request.GetIdempotencyKey() is not null).Should().Be(accepted, $"the server {(accepted ? "accepts" : "refuses")} {what}");
            pattern.IsMatch(value).Should().Be(accepted, $"the schema {(accepted ? "admits" : "refuses")} {what}, as the server does");
        }
    }

    [Fact]
    public async Task RequireIfMatch_DocumentsARequiredHeader_And428_400_412()
    {
        var document = await GetDocumentAsync();
        var operation = document.Operation("/v1/orders/{id}", "put");
        var parameter = HeaderParameter(operation, IfMatchHeader);

        parameter.Should().NotBeNull();
        parameter!["required"]!.GetValue<bool>().Should().BeTrue();
        parameter["schema"]!["type"]!.GetValue<string>().Should().Be("string");

        // RFC 9110 section 13.1.1 as the core applies it: missing or *, malformed or several tags, weak or stale.
        ProblemResponseDescription(operation, "428").Should().Contain(PresentationErrorCodes.PreconditionRequired);
        ProblemResponseDescription(operation, "400").Should()
            .Contain(PresentationErrorCodes.PreconditionInvalid)
            .And.NotContain(PresentationErrorCodes.IdempotencyKeyRequired);
        ProblemResponseDescription(operation, "412").Should().Contain(PresentationErrorCodes.PreconditionFailed);
    }

    [Fact]
    public async Task BothHeaders_DocumentOne400_ThatNamesTheRefusalsOfBoth()
    {
        var operation = (await GetDocumentAsync()).Operation("/v1/orders/{id}/refunds", "post");

        HeaderParameter(operation, IdempotencyKeyHeader)!["required"]!.GetValue<bool>().Should().BeTrue();
        HeaderParameter(operation, IfMatchHeader)!["required"]!.GetValue<bool>().Should().BeTrue();
        ProblemResponseDescription(operation, "400").Should()
            .Contain(PresentationErrorCodes.IdempotencyKeyRequired)
            .And.Contain(PresentationErrorCodes.IdempotencyKeyInvalid)
            .And.Contain(PresentationErrorCodes.PreconditionInvalid);
        operation["responses"]!["412"].Should().NotBeNull();
        operation["responses"]!["428"].Should().NotBeNull();
    }

    [Fact]
    public async Task PlatformResponses_FollowTheOperationsOwn_InStatusOrder()
    {
        var operation = (await GetDocumentAsync()).Operation("/v1/orders/{id}", "put");

        operation["responses"]!.AsObject().Select(response => response.Key)
            .Should().Equal("204", "400", "401", "403", "412", "428", "default");
    }

    [Fact]
    public async Task ParameterTypes_AddTheRequirementMetadata_TheConventionsAdd()
    {
        // What the documents read: the core adds the metadata for a parameter as it does for the convention.
        await using var app = await OpenApiTestHost.StartAsync(OrdersApi.Map);
        var endpoints = app.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToArray();

        Metadata("/orders/{id:int}/payments", HttpMethods.Post).GetMetadata<IIdempotencyKeyRequiredMetadata>()
            .Should().NotBeNull("an IdempotencyKey parameter requires the header");
        Metadata("/orders/{id:int}/cancellation", HttpMethods.Post).GetMetadata<IIdempotencyKeyRequiredMetadata>()
            .Should().NotBeNull("an IdempotencyKey parameter requires the header");
        Metadata("/orders/{id:int}", HttpMethods.Patch).GetMetadata<IIfMatchRequiredMetadata>()
            .Should().NotBeNull("an IfMatch<TVersion> parameter requires the header");

        EndpointMetadataCollection Metadata(string pathEnd, string method) => endpoints.Single(endpoint =>
            endpoint.RoutePattern.RawText!.EndsWith(pathEnd, StringComparison.Ordinal)
            && endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Contains(method) == true).Metadata;
    }

    [Theory]
    [InlineData("/v1/orders/{id}/payments", "post")]
    [InlineData("/v1/orders/{id}/cancellation", "post")]
    public async Task IdempotencyKeyParameter_IsDocumentedExactlyLikeTheConvention(string path, string method)
    {
        var document = await GetDocumentAsync();
        var convention = document.Operation("/v1/orders", "post");
        var parameter = document.Operation(path, method);

        Json(HeaderParameter(parameter, IdempotencyKeyHeader)).Should().Be(Json(HeaderParameter(convention, IdempotencyKeyHeader)));
        Json(parameter["responses"]!["400"]).Should().Be(Json(convention["responses"]!["400"]));
    }

    [Theory]
    [InlineData("patch")]
    [InlineData("delete")]
    public async Task IfMatchParameterAndLambdaAttribute_AreDocumentedExactlyLikeTheConvention(string method)
    {
        // PATCH declares an IfMatch<long> parameter, DELETE carries [RequireIfMatch] on its lambda, PUT the convention.
        var document = await GetDocumentAsync();
        var convention = document.Operation("/v1/orders/{id}", "put");
        var operation = document.Operation("/v1/orders/{id}", method);

        Json(HeaderParameter(operation, IfMatchHeader)).Should().Be(Json(HeaderParameter(convention, IfMatchHeader)));

        foreach (var status in new[] { "400", "412", "428" })
        {
            Json(operation["responses"]![status]).Should().Be(Json(convention["responses"]![status]), $"{method} documents {status} as PUT does");
        }
    }

    [Theory]
    [InlineData("/v1/orders/{id}/payments", "post", true)]
    [InlineData("/v1/orders/{id}/cancellation", "post", false)]
    [InlineData("/v1/orders/{id}", "patch", true)]
    [InlineData("/v1/orders/{id}/refunds", "post", false)]
    public async Task ParameterTypes_AreDocumentedAsTheirHeaderOnly_NeverAsABodyOrQueryValue(string path, string method, bool hasBody)
    {
        var document = await GetDocumentAsync();
        var operation = document.Operation(path, method);

        operation["parameters"]!.AsArray()
            .Select(parameter => $"{parameter!["in"]!.GetValue<string>()} {parameter["name"]!.GetValue<string>()}")
            .Should().BeSubsetOf(["path id", $"header {IdempotencyKeyHeader}", $"header {IfMatchHeader}"]);

        if (hasBody)
        {
            var content = operation["requestBody"]!["content"]!.AsObject();

            content.Select(media => media.Key).Should().Equal("application/json");
            content["application/json"]!["schema"]!["$ref"]!.GetValue<string>().Should().Be("#/components/schemas/Order");
        }
        else
        {
            operation["requestBody"].Should().BeNull("an IdempotencyKey or IfMatch<T> parameter is never inferred as the body");
        }

        document["components"]!["schemas"]!.AsObject().Select(schema => schema.Key)
            .Should().NotContain(name => name.Contains("IdempotencyKey", StringComparison.Ordinal) || name.Contains("IfMatch", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EndpointsWithoutTheRequirement_DocumentNeitherHeader()
    {
        var document = await GetDocumentAsync();

        foreach (var (path, method) in new[] { ("/v1/orders", "get"), ("/v1/orders/{id}", "get") })
        {
            var operation = document.Operation(path, method);

            HeaderParameter(operation, IdempotencyKeyHeader).Should().BeNull($"{method} {path} does not require a key");
            HeaderParameter(operation, IfMatchHeader).Should().BeNull($"{method} {path} does not require If-Match");
            operation["responses"]!["400"].Should().BeNull();
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

        var create = document.Operation("/v1/mvc/orders", "post");
        HeaderParameter(create, IdempotencyKeyHeader)!["required"]!.GetValue<bool>().Should().BeTrue();
        create["responses"]!["400"].Should().NotBeNull();

        var update = document.Operation("/v1/mvc/orders/{id}", "put");
        HeaderParameter(update, IfMatchHeader)!["required"]!.GetValue<bool>().Should().BeTrue();
        update["responses"]!["400"].Should().NotBeNull();
        update["responses"]!["412"].Should().NotBeNull();
        update["responses"]!["428"].Should().NotBeNull();
    }

    private static JsonNode? HeaderParameter(JsonNode operation, string name) =>
        operation["parameters"]?.AsArray().SingleOrDefault(parameter =>
            parameter!["in"]!.GetValue<string>() == "header"
            && string.Equals(parameter["name"]!.GetValue<string>(), name, StringComparison.OrdinalIgnoreCase));

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

    private static async Task<JsonNode> GetDocumentAsync()
    {
        await using var app = await OpenApiTestHost.StartAsync(OrdersApi.Map);
        using var client = app.GetTestClient();

        return await client.GetDocumentAsync("v1");
    }
}
