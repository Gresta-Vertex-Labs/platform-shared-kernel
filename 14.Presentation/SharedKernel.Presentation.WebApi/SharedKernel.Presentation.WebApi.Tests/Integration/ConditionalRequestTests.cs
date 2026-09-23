using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using SharedKernel.Core.Exceptions;
using SharedKernel.Persistence.Abstractions.Repositories;
using SharedKernel.Presentation.WebApi.Errors;
using SharedKernel.Presentation.WebApi.Tests.TestSupport;
using SharedKernel.Primitives.Results;
using Xunit;
using HeaderNames = Microsoft.Net.Http.Headers.HeaderNames;

namespace SharedKernel.Presentation.WebApi.Tests.Integration;

/// <summary>
/// Design D10/D16 and B13: ETag with 304 on reads, a required <c>If-Match</c> (428 when missing) and 412 for a stale
/// version on writes — for a returned conflict and a thrown one — round-tripping the persistence layer's
/// <see cref="EntityVersion"/>.
/// </summary>
public sealed class ConditionalRequestTests : IClassFixture<FullStackHost>, IAsyncLifetime
{
    private static readonly EntityVersion CurrentVersion = EntityVersion.FromRowVersion(42);

    private readonly FullStackHost _host;
    private WebApplication? _app;

    public ConditionalRequestTests(FullStackHost host)
    {
        _host = host;
    }

    private HttpClient Client => _app!.GetTestClient();

    public async Task InitializeAsync()
    {
        _app = await WebApiTestHost.StartAsync(app =>
        {
            app.MapGet("/orders/1", () => Result<Order>.Success(new Order("1", CurrentVersion)).ToOkWithETag(order => order.Version.ToString()));
            app.MapGet("/orders/1/summary", () => Result<Order>.Success(new Order("1", CurrentVersion))
                .ToOkWithETag(order => order.Id, order => order.Version.ToString()));
            app.MapPost("/orders/1/touch", () => Result<Order>.Success(new Order("1", CurrentVersion)).ToOkWithETag(order => order.Version.ToString()));
            app.MapPut("/orders/1", (HttpContext context) =>
                    EntityVersion.TryParse(context.GetIfMatch(), out var expected) && expected == CurrentVersion
                        ? Result.Success().ToNoContent()
                        : Result.Failure(TestErrors.VersionConflict).ToNoContent())
                .RequireIfMatch();
            app.MapGet("/if-match", (HttpContext context) => context.GetIfMatch() ?? "(none)");
            app.MapGet("/etag", (HttpContext context) =>
            {
                context.Response.SetETag("7");
                return "ok";
            });
        });
    }

    public async Task DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Read_SendsTheVersionAsAStrongETag()
    {
        using var response = await Client.GetAsync("/orders/1");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.ETag.Should().Be(new EntityTagHeaderValue("\"42\""));
        (await response.Content.ReadFromJsonAsync<Order>())!.Id.Should().Be("1");
    }

    [Theory]
    [InlineData("\"42\"")]
    [InlineData("W/\"42\"")]
    [InlineData("\"7\", \"42\"")]
    [InlineData("*")]
    public async Task Read_WhoseIfNoneMatchNamesTheVersion_Is304_WithTheETag_AndNoBody(string ifNoneMatch)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/orders/1");
        request.Headers.TryAddWithoutValidation(HeaderNames.IfNoneMatch, ifNoneMatch);

        using var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotModified);
        response.Headers.ETag.Should().Be(new EntityTagHeaderValue("\"42\""));
        (await response.Content.ReadAsByteArrayAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Read_WhoseIfNoneMatchNamesAnotherVersion_Is200()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/orders/1/summary");
        request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue("\"41\""));

        using var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<string>()).Should().Be("1");
    }

    [Fact]
    public async Task NonReadMethod_IsNever304()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/orders/1/touch");
        request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue("\"42\""));

        using var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task B13_Write_WithTheCurrentVersion_Succeeds()
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, "/orders/1");
        request.Headers.IfMatch.Add(new EntityTagHeaderValue("\"42\""));

        using var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Write_WithAStaleVersion_Is412_KeepingItsErrorCode()
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, "/orders/1");
        request.Headers.IfMatch.Add(new EntityTagHeaderValue("\"41\""));

        using var response = await Client.SendAsync(request);

        await response.ShouldBeProblemAsync(StatusCodes.Status412PreconditionFailed, "order.version_conflict");
    }

    [Fact]
    public async Task Write_WithoutIfMatch_Is428()
    {
        using var response = await Client.PutAsync("/orders/1", content: null);

        await response.ShouldBeProblemAsync(StatusCodes.Status428PreconditionRequired, PresentationErrorCodes.PreconditionRequired);
    }

    [Fact]
    public async Task Write_WithAMalformedIfMatch_Is428()
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, "/orders/1");
        request.Headers.TryAddWithoutValidation(HeaderNames.IfMatch, "42");

        using var response = await Client.SendAsync(request);

        await response.ShouldBeProblemAsync(StatusCodes.Status428PreconditionRequired, PresentationErrorCodes.PreconditionRequired);
    }

    [Fact]
    public async Task ThrownConflict_OnAnIfMatchEndpoint_Is412()
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, "/versioned-throw");
        request.Headers.IfMatch.Add(new EntityTagHeaderValue("\"1\""));

        using var response = await _host.Client.SendAsync(request);

        await response.ShouldBeProblemAsync(StatusCodes.Status412PreconditionFailed, "order.version_conflict");
    }

    [Fact]
    public async Task Conflict_OnAnEndpointWithoutIfMatch_Stays409()
    {
        using var response = await _host.Client.PutAsync("/not-versioned", content: null);

        await response.ShouldBeProblemAsync(StatusCodes.Status409Conflict, "order.version_conflict");
    }

    [Theory]
    [InlineData("/mvc-api/versioned", "\"2\"", 412)]
    [InlineData("/mvc-api/versioned-throw", "\"1\"", 412)]
    [InlineData("/mvc-api/versioned", null, 428)]
    public async Task MvcAttribute_RequiresIfMatch_AndReports412(string path, string? ifMatch, int status)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, path);
        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation(HeaderNames.IfMatch, ifMatch);
        }

        using var response = await _host.Client.SendAsync(request);

        await response.ShouldBeProblemAsync(
            status,
            status == 428 ? PresentationErrorCodes.PreconditionRequired : "order.version_conflict");
    }

    [Fact]
    public async Task MvcAttribute_WithTheCurrentVersion_Succeeds()
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, "/mvc-api/versioned");
        request.Headers.IfMatch.Add(new EntityTagHeaderValue("\"1\""));

        using var response = await _host.Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Theory]
    [InlineData("\"42\"", "42")]
    [InlineData("W/\"42\"", "42")]
    [InlineData("\"a\", \"b\"", "a")]
    [InlineData("*", "*")]
    [InlineData("unquoted", "(none)")]
    public async Task GetIfMatch_ReturnsTheFirstEntityTag_WithoutQuotesOrWeakPrefix(string header, string expected)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/if-match");
        request.Headers.TryAddWithoutValidation(HeaderNames.IfMatch, header);

        using var response = await Client.SendAsync(request);

        (await response.Content.ReadAsStringAsync()).Should().Be(expected);
    }

    [Fact]
    public async Task GetIfMatch_WithoutTheHeader_IsNull()
    {
        using var response = await Client.GetAsync("/if-match");

        (await response.Content.ReadAsStringAsync()).Should().Be("(none)");
    }

    [Fact]
    public async Task SetETag_WritesAStrongEntityTag()
    {
        using var response = await Client.GetAsync("/etag");

        response.Headers.ETag.Should().Be(new EntityTagHeaderValue("\"7\""));
    }

    [Theory]
    [InlineData("")]
    [InlineData("a\"b")]
    [InlineData("a b")]
    public void SetETag_RefusesAVersionThatCannotBeAnEntityTag(string version)
    {
        var context = new DefaultHttpContext();

        var act = () => context.Response.SetETag(version);

        act.Should().Throw<ArgumentException>();
    }

    public sealed record Order(string Id, EntityVersion Version);
}
