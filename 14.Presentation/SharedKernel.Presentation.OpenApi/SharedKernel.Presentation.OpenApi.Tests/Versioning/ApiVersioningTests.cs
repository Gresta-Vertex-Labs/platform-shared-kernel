using System.Net;
using System.Text.Json.Nodes;
using Asp.Versioning;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using SharedKernel.Presentation.OpenApi.Tests.TestSupport;
using SharedKernel.Presentation.OpenApi.Versioning;
using SharedKernel.Presentation.WebApi.Errors;
using SharedKernel.Primitives.Propagation;
using Xunit;

namespace SharedKernel.Presentation.OpenApi.Tests.Versioning;

/// <summary>The platform versioning defaults, and API versioning's own errors in the platform's problem shape.</summary>
public sealed class ApiVersioningTests
{
    private const string VersionHeader = "X-Api-Version";

    [Fact]
    public async Task RequestNamingNoVersion_IsServedTheDefaultVersion()
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();

        (await client.GetStringAsync("/items")).Should().Be("1.0");
    }

    [Fact]
    public async Task VersionHeader_SelectsTheVersion()
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/items");
        request.Headers.Add(VersionHeader, "2.0");

        using var response = await client.SendAsync(request);

        (await response.Content.ReadAsStringAsync()).Should().Be("2.0");
        response.Headers.GetValues("api-supported-versions").Should().ContainSingle().Which.Should().Be("1.0, 2.0");
    }

    [Fact]
    public async Task VersioningHook_RunsAfterThePlatformDefaults()
    {
        await using var app = await StartAsync(options => options.Versioning = versioning => versioning.DefaultApiVersion = new ApiVersion(2, 0));
        using var client = app.GetTestClient();

        (await client.GetStringAsync("/items")).Should().Be("2.0");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnsupportedVersion_IsAPlatformProblem_WhicheverSetupRunsFirst(bool openApiFirst)
    {
        await using var app = await OpenApiTestHost.StartAsync(MapItems, openApiFirst: openApiFirst);
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/items");
        request.Headers.Add(VersionHeader, "9.0");
        request.Headers.Add(WellKnownHeaders.CorrelationId, "corr-123");

        using var response = await client.SendAsync(request);
        var problem = await ReadProblemAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        problem["type"]!.GetValue<string>().Should().Be("https://tools.ietf.org/html/rfc9110#section-15.5.1");
        problem["title"]!.GetValue<string>().Should().Be("Bad Request");
        problem["status"]!.GetValue<int>().Should().Be(400);
        problem["detail"]!.GetValue<string>().Should().Contain("9.0");
        problem["instance"]!.GetValue<string>().Should().Be("/items");
        problem[ProblemDetailsExtensionNames.ErrorCode]!.GetValue<string>().Should().Be("http.400");
        problem[ProblemDetailsExtensionNames.TraceId].Should().NotBeNull();
        problem[ProblemDetailsExtensionNames.CorrelationId]!.GetValue<string>().Should().Be("corr-123");
        problem[ApiVersioningProblems.CodeMember].Should().BeNull("the platform's errorCode is the one code of an error");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnsupportedVersion_UsesTheConfiguredTypeBaseUri_WhicheverSetupRunsFirst(bool openApiFirst)
    {
        await using var app = await OpenApiTestHost.StartAsync(
            MapItems,
            configureWebApi: options => options.Problems.TypeBaseUri = new Uri("https://errors.example.com/"),
            openApiFirst: openApiFirst);
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/items");
        request.Headers.Add(VersionHeader, "9.0");

        using var response = await client.SendAsync(request);
        var problem = await ReadProblemAsync(response);

        problem["type"]!.GetValue<string>().Should().Be("https://errors.example.com/http.400");
        problem["title"]!.GetValue<string>().Should().Be("Bad Request");
        problem[ApiVersioningProblems.CodeMember].Should().BeNull();
    }

    [Fact]
    public async Task MalformedVersion_IsAPlatformProblem()
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/items");
        request.Headers.Add(VersionHeader, "not-a-version");

        using var response = await client.SendAsync(request);
        var problem = await ReadProblemAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        problem["title"]!.GetValue<string>().Should().Be("Bad Request");
        problem[ProblemDetailsExtensionNames.ErrorCode]!.GetValue<string>().Should().Be("http.400");
        problem[ApiVersioningProblems.CodeMember].Should().BeNull();
    }

    [Fact]
    public void Normalize_LeavesOtherProblemsAlone()
    {
        var problem = new ProblemDetails { Status = 409, Title = "Conflict", Type = "https://example.com/conflict" };
        problem.Extensions[ApiVersioningProblems.CodeMember] = "some.other.code";

        ApiVersioningProblems.Normalize(problem);

        problem.Title.Should().Be("Conflict");
        problem.Type.Should().Be("https://example.com/conflict");
        problem.Extensions.Should().ContainKey(ApiVersioningProblems.CodeMember);
    }

    private static Task<WebApplication> StartAsync(Action<Options.SharedKernelOpenApiOptions>? configure = null) =>
        OpenApiTestHost.StartAsync(MapItems, configure);

    private static void MapItems(WebApplication app) =>
        app.NewVersionedApi("Items")
            .MapGroup("/items")
            .HasApiVersion(1.0)
            .HasApiVersion(2.0)
            .MapGet("/", (ApiVersion version) => version.ToString());

    private static async Task<JsonNode> ReadProblemAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();

        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json", body);
        return JsonNode.Parse(body)!;
    }
}
