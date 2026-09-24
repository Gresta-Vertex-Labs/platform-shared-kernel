using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;
using SharedKernel.Presentation.OpenApi.Tests.TestSupport;
using SharedKernel.Presentation.WebApi;
using SharedKernel.Primitives.Results;
using Xunit;

namespace SharedKernel.Presentation.OpenApi.Tests.Documents;

/// <summary>
/// The <c>ETag</c> header an <c>OkWithETag&lt;T&gt;</c> result sends, declared on the responses that carry it: 200,
/// and 304 for a read — and on no other response.
/// </summary>
public sealed class ETagDocumentationTests : IAsyncLifetime
{
    private const string DocumentPath = "/v1/documents/{id}";

    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private JsonNode _document = null!;

    public async Task InitializeAsync()
    {
        _app = await OpenApiTestHost.StartAsync(app =>
        {
            var documents = app.NewVersionedApi("Documents")
                .MapGroup("/v{version:apiVersion}/documents")
                .HasApiVersion(1.0);

            documents.MapGet("/", () => Result<Order[]>.Success([new Order(1)]).ToOk());
            documents.MapGet("/{id:int}", (int id) => Result<Order>.Success(new Order(id)).ToOkWithETag(VersionOf));
            documents.MapPut("/{id:int}", (int id, Order order) => Result<Order>.Success(order).ToOkWithETag(VersionOf));
        });

        _client = _app.GetTestClient();
        _document = await _client.GetDocumentAsync("v1");
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task Read_DeclaresTheETag_On200And304_WhereTheServerSendsIt()
    {
        var responses = _document.Operation(DocumentPath, "get")["responses"]!;

        ShouldDeclareETag(responses["200"]);
        ShouldDeclareETag(responses["304"]);

        // What the server sends: the ETag with the body, and again on the 304 of a client that has that version.
        using var ok = await _client.GetAsync("/v1/documents/7");
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        ok.Headers.ETag.Should().NotBeNull();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/documents/7");
        request.Headers.IfNoneMatch.Add(ok.Headers.ETag!);
        using var notModified = await _client.SendAsync(request);
        notModified.StatusCode.Should().Be(HttpStatusCode.NotModified);
        notModified.Headers.ETag.Should().Be(ok.Headers.ETag);
    }

    [Fact]
    public void Write_DeclaresTheETag_On200_AndHasNo304()
    {
        var responses = _document.Operation(DocumentPath, "put")["responses"]!;

        ShouldDeclareETag(responses["200"]);
        responses["304"].Should().BeNull("only a GET or HEAD is answered 304");
    }

    [Fact]
    public void OtherResponses_DeclareNoETag()
    {
        _document.Operation("/v1/documents", "get")["responses"]!["200"]!["headers"]
            .Should().BeNull("ToOk sends no ETag");

        foreach (var method in new[] { "get", "put" })
        {
            foreach (var (status, response) in _document.Operation(DocumentPath, method)["responses"]!.AsObject())
            {
                if (status is not ("200" or "304"))
                {
                    response!["headers"].Should().BeNull($"{method} {DocumentPath} sends no ETag with {status}");
                }
            }
        }
    }

    [Fact]
    public async Task MvcActionReturningOkWithETag_DeclaresTheETag_LikeAMinimalApi()
    {
        await using var app = await OpenApiTestHost.StartAsync(
            app => app.MapControllers(),
            configureBuilder: builder => builder.Services.AddControllers().AddApplicationPart(typeof(OrdersApi).Assembly));
        using var client = app.GetTestClient();

        var responses = (await client.GetDocumentAsync("v1")).Operation("/v1/mvc/orders/{id}/receipt", "get")["responses"]!;

        ShouldDeclareETag(responses["200"]);
        ShouldDeclareETag(responses["304"]);
    }

    private static string VersionOf(Order order) => order.Id.ToString(CultureInfo.InvariantCulture);

    private static void ShouldDeclareETag(JsonNode? response)
    {
        response.Should().NotBeNull("the operation documents the response");

        var header = response!["headers"]?[HeaderNames.ETag];
        header.Should().NotBeNull($"the response carries an ETag: {response.ToJsonString()}");
        header!["required"]!.GetValue<bool>().Should().BeTrue("the result always sends it");
        header["schema"]!["type"]!.GetValue<string>().Should().Be("string");
        header["description"]!.GetValue<string>().Should().Contain("If-None-Match").And.Contain("If-Match");
    }
}
