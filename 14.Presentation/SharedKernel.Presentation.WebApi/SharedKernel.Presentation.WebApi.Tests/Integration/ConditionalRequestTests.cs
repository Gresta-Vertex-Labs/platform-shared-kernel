using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Core.Exceptions;
using SharedKernel.Persistence.Abstractions.Repositories;
using SharedKernel.Presentation.WebApi.Tests.TestSupport;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using Xunit;
using HeaderNames = Microsoft.Net.Http.Headers.HeaderNames;

namespace SharedKernel.Presentation.WebApi.Tests.Integration;

/// <summary>
/// Design D10/D16 and B13, refined by R6–R8, R14 and R18: ETag with 304 on reads; a required <c>If-Match</c> checked
/// for every kind of endpoint (428 missing or <c>*</c>, 400 malformed or several tags, 412 weak or unparsable); and 412
/// decided from the error — a version conflict of a conditional request — round-tripping the persistence layer's
/// <see cref="EntityVersion"/>, whose text is an opaque token since P-562 X4 (<see cref="TestVersions"/>). J1:
/// <c>GetIfMatch()</c> returns one strong tag or nothing, and binding never reads an unusable header as a missing one
/// (the accepted, optional form is in <see cref="OptionalHeaderTests"/>).
/// </summary>
public sealed class ConditionalRequestTests : IClassFixture<FullStackHost>, IAsyncLifetime
{
    private const string CurrentTag = "\"" + TestVersions.Current + "\"";
    private const string StaleTag = "\"" + TestVersions.Stale + "\"";

    private static readonly EntityVersion CurrentVersion = TestVersions.CurrentVersion;

    private readonly FullStackHost _host;
    private WebApplication? _app;

    public ConditionalRequestTests(FullStackHost host)
    {
        _host = host;
    }

    private HttpClient Client => _app!.GetTestClient();

    public async Task InitializeAsync()
    {
        _app = await WebApiTestHost.StartAsync(
            app =>
            {
                app.MapGet("/orders/1", () => Result<Order>.Success(new Order("1", CurrentVersion)).ToOkWithETag(order => order.Version.ToString()));
                app.MapGet("/orders/1/summary", () => Result<Order>.Success(new Order("1", CurrentVersion))
                    .ToOkWithETag(order => order.Version.ToString(), order => order.Id));
                app.MapPost("/orders/1/touch", () => Result<Order>.Success(new Order("1", CurrentVersion)).ToOkWithETag(order => order.Version.ToString()));
                app.MapPut("/orders/1", (HttpContext context) =>
                        EntityVersion.TryParse(context.GetIfMatch(), out var expected) && expected == CurrentVersion
                            ? Result.Success().ToNoContent()
                            : Result.Failure(TestErrors.StaleVersion).ToNoContent())
                    .RequireIfMatch();
                app.MapPut("/files/report", () => Result.Failure(Error.Conflict("storage.already_exists", "The object exists.")).ToNoContent());
                app.MapPut("/orders/2", () => Result.Failure(Error.Conflict("orders.stale", "Stale.")).ToNoContent());
                app.MapGet("/if-match", (HttpContext context) => context.GetIfMatch() ?? "(none)");
                app.MapGet("/if-match-tags", (HttpContext context) =>
                    string.Join(";", EntityTags.GetIfMatchTags(context.Request).Select(tag => $"{tag.Tag}:{tag.IsWeak}")));
                app.MapGet("/etag", (HttpContext context) =>
                {
                    context.Response.SetETag("7");
                    return "ok";
                });
            },
            configureOptions: options => options.Problems.PreconditionFailedErrorCodes.Add("orders.stale"));
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
        response.Headers.ETag.Should().Be(new EntityTagHeaderValue(CurrentTag));
        var body = (await response.Content.ReadFromJsonAsync<Order>())!;
        body.Id.Should().Be("1");
        body.Version.Should().Be(CurrentVersion, "a version in a body is its token too");
    }

    [Theory]
    [InlineData(CurrentTag)]
    [InlineData("W/" + CurrentTag)]
    [InlineData(StaleTag + ", " + CurrentTag)]
    [InlineData("*")]
    public async Task Read_WhoseIfNoneMatchNamesTheVersion_Is304_WithTheETag_AndNoBody(string ifNoneMatch)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/orders/1");
        request.Headers.TryAddWithoutValidation(HeaderNames.IfNoneMatch, ifNoneMatch);

        using var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotModified);
        response.Headers.ETag.Should().Be(new EntityTagHeaderValue(CurrentTag));
        (await response.Content.ReadAsByteArrayAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Read_WhoseIfNoneMatchNamesAnotherVersion_Is200()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/orders/1/summary");
        request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(StaleTag));

        using var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<string>()).Should().Be("1");
    }

    [Fact]
    public async Task NonReadMethod_IsNever304()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/orders/1/touch");
        request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(CurrentTag));

        using var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("/orders/1", true)]
    [InlineData("/orders/1/touch", false)]
    public void R14_OpenApiDocuments304_OnlyForReads(string route, bool documents304)
    {
        var endpoint = _app!.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Single(candidate => candidate.RoutePattern.RawText == route && candidate.Metadata.GetMetadata<IHttpMethodMetadata>()!
                .HttpMethods.Contains(documents304 ? HttpMethods.Get : HttpMethods.Post));

        var statuses = endpoint.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>().Select(metadata => metadata.StatusCode);

        statuses.Should().Contain(StatusCodes.Status200OK);
        statuses.Contains(StatusCodes.Status304NotModified).Should().Be(documents304);
    }

    [Fact]
    public async Task B13_Write_WithTheCurrentVersion_Succeeds()
    {
        using var response = await PutAsync(Client, "/orders/1", CurrentTag);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Write_WithAStaleVersion_Is412_KeepingItsErrorCode()
    {
        using var response = await PutAsync(Client, "/orders/1", StaleTag);

        await response.ShouldBeProblemAsync(StatusCodes.Status412PreconditionFailed, TestErrors.ConcurrencyConflictCode);
    }

    [Fact]
    public async Task X4_Write_WithARawRowVersion_Is412_ForItIsNotAVersion()
    {
        using var response = await PutAsync(Client, "/orders/1", "\"42\"");

        await response.ShouldBeProblemAsync(StatusCodes.Status412PreconditionFailed, TestErrors.ConcurrencyConflictCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("*")]
    public async Task R8_Write_WithoutASpecificTag_Is428(string? ifMatch)
    {
        using var response = await PutAsync(Client, "/orders/1", ifMatch);

        await response.ShouldBeProblemAsync(StatusCodes.Status428PreconditionRequired, PresentationErrorCodes.PreconditionRequired);
    }

    [Theory]
    [InlineData("42")]
    [InlineData("\"42")]
    [InlineData("\"41\", \"42\"")]
    [InlineData("*, \"42\"")]
    public async Task R8_Write_WithAMalformedIfMatch_OrSeveralTags_Is400(string ifMatch)
    {
        using var response = await PutAsync(Client, "/orders/1", ifMatch);

        await response.ShouldBeProblemAsync(StatusCodes.Status400BadRequest, PresentationErrorCodes.PreconditionInvalid);
    }

    [Fact]
    public async Task R8_Write_WithAWeakTag_Is412_BecauseIfMatchComparesStrongly()
    {
        using var response = await PutAsync(Client, "/orders/1", "W/" + CurrentTag);

        await response.ShouldBeProblemAsync(StatusCodes.Status412PreconditionFailed, PresentationErrorCodes.PreconditionFailed);
    }

    [Theory]
    [InlineData("/versioned")]
    [InlineData("/versioned-attribute")]
    [InlineData("/versioned-parameter")]
    [InlineData("/mvc-api/versioned")]
    public async Task R6_RequiredIfMatch_IsEnforced_ForEveryKindOfEndpoint(string path)
    {
        using var missing = await PutAsync(_host.Client, path, ifMatch: null);
        using var weak = await PutAsync(_host.Client, path, "W/" + CurrentTag);
        using var current = await PutAsync(_host.Client, path, CurrentTag);
        using var stale = await PutAsync(_host.Client, path, StaleTag);

        await missing.ShouldBeProblemAsync(StatusCodes.Status428PreconditionRequired, PresentationErrorCodes.PreconditionRequired);
        await weak.ShouldBeProblemAsync(StatusCodes.Status412PreconditionFailed, PresentationErrorCodes.PreconditionFailed);
        current.StatusCode.Should().Be(HttpStatusCode.NoContent);
        await stale.ShouldBeProblemAsync(StatusCodes.Status412PreconditionFailed, TestErrors.ConcurrencyConflictCode);
    }

    [Fact]
    public async Task R18_IfMatchParameter_RefusesATagThatIsNotAVersion_With412()
    {
        using var response = await PutAsync(_host.Client, "/versioned-parameter", "\"not-a-version\"");

        await response.ShouldBeProblemAsync(StatusCodes.Status412PreconditionFailed, PresentationErrorCodes.PreconditionFailed);
    }

    [Theory]
    [InlineData("/versioned")]
    [InlineData("/versioned-attribute")]
    [InlineData("/versioned-parameter")]
    [InlineData("/mvc-api/versioned")]
    public void R18_EveryDeclaration_CarriesTheMetadataOpenApiReads(string route)
    {
        var endpoint = _host.App.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Single(candidate => "/" + candidate.RoutePattern.RawText!.TrimStart('/') == route);

        endpoint.Metadata.GetMetadata<IIfMatchRequiredMetadata>().Should().NotBeNull();
    }

    [Fact]
    public void R18_IfMatch_IsConstructibleInUnitTests()
    {
        var ifMatch = new IfMatch<EntityVersion>(CurrentVersion);

        ifMatch.Version.Should().Be(CurrentVersion);
        ifMatch.ToString().Should().Be(TestVersions.Current);
        ifMatch.Should().Be(new IfMatch<EntityVersion>(EntityVersion.Parse(TestVersions.Current)));
        FluentActions.Invoking(() => new IfMatch<string>(null!)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task R18_IfMatchBinding_WithoutThePipeline_YieldsTheVersionOfOneStrongParsableTag()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.IfMatch = CurrentTag;

        var bound = await IfMatch<EntityVersion>.BindAsync(context);

        bound.Should().NotBeNull();
        bound!.Version.Should().Be(CurrentVersion);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task J1_IfMatchBinding_WithoutTheHeader_IsNull(string? header)
    {
        var context = new DefaultHttpContext();
        if (header is not null)
        {
            context.Request.Headers.IfMatch = header;
        }

        (await IfMatch<EntityVersion>.BindAsync(context)).Should().BeNull();
    }

    [Theory]
    [InlineData("W/" + CurrentTag)]
    [InlineData("\"a\", " + CurrentTag)]
    [InlineData("\"abc\"")]
    [InlineData("\"42\"")] // X4: a raw row version is not a version
    [InlineData("*")]
    [InlineData("unquoted")]
    public async Task J1_IfMatchBinding_WithoutThePipeline_RefusesAnUnusableHeader_RatherThanBindItAsMissing(string header)
    {
        // Without UseSharedKernelWebApi() nothing refuses the header before binding; null would read it as missing,
        // which a nullable parameter takes for "unconditional".
        var context = new DefaultHttpContext();
        context.Request.Headers.IfMatch = header;

        var bind = async () => await IfMatch<EntityVersion>.BindAsync(context);

        (await bind.Should().ThrowAsync<BadHttpRequestException>()).Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task ThrownConflict_OnAnIfMatchEndpoint_Is412()
    {
        using var response = await PutAsync(_host.Client, "/versioned-throw", "\"1\"");

        await response.ShouldBeProblemAsync(StatusCodes.Status412PreconditionFailed, TestErrors.ConcurrencyConflictCode);
    }

    [Fact]
    public async Task R7_VersionConflict_OfARequestWithoutConditionalHeaders_Stays409()
    {
        using var response = await _host.Client.PutAsync("/not-versioned", content: null);

        await response.ShouldBeProblemAsync(StatusCodes.Status409Conflict, TestErrors.ConcurrencyConflictCode);
    }

    [Fact]
    public async Task R7_VersionConflict_OfAConditionalRequest_Is412_EvenWithoutTheRequirement()
    {
        using var response = await PutAsync(_host.Client, "/not-versioned", "\"1\"");

        await response.ShouldBeProblemAsync(StatusCodes.Status412PreconditionFailed, TestErrors.ConcurrencyConflictCode);
    }

    [Fact]
    public async Task R7_OtherConflict_OfAConditionalRequest_Stays409()
    {
        // A duplicate name or a forbidden state is not the version the client named being stale.
        using var response = await PutAsync(_host.Client, "/versioned-other-conflict", "\"1\"");

        await response.ShouldBeProblemAsync(StatusCodes.Status409Conflict, "order.version_conflict");
    }

    [Fact]
    public async Task R7_CreateOnly_WithIfNoneMatchStar_FindingTheObject_Is412()
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, "/files/report");
        request.Headers.TryAddWithoutValidation(HeaderNames.IfNoneMatch, "*");

        using var response = await Client.SendAsync(request);

        await response.ShouldBeProblemAsync(StatusCodes.Status412PreconditionFailed, "storage.already_exists");
    }

    [Fact]
    public async Task R7_ConfiguredCode_IsAVersionConflictToo()
    {
        using var withHeader = await PutAsync(Client, "/orders/2", "\"1\"");
        using var withoutHeader = await Client.PutAsync("/orders/2", content: null);

        await withHeader.ShouldBeProblemAsync(StatusCodes.Status412PreconditionFailed, "orders.stale");
        await withoutHeader.ShouldBeProblemAsync(StatusCodes.Status409Conflict, "orders.stale");
    }

    [Fact]
    public async Task MvcAttribute_ThrownConflict_Is412()
    {
        using var response = await PutAsync(_host.Client, "/mvc-api/versioned-throw", "\"1\"");

        await response.ShouldBeProblemAsync(StatusCodes.Status412PreconditionFailed, TestErrors.ConcurrencyConflictCode);
    }

    [Theory]
    [InlineData("\"42\"", "42")]
    [InlineData("\"\"", "")] // an empty strong tag names a version that is never current, never "no header"
    [InlineData("*", "*")]
    [InlineData("W/\"42\"", "(none)")] // If-Match compares strongly: a weak tag names nothing to act on
    [InlineData("\"a\", \"b\"", "(none)")] // never the first of several
    [InlineData("*, \"42\"", "(none)")]
    [InlineData("unquoted", "(none)")]
    public async Task J1_GetIfMatch_ReturnsTheOneStrongTag_OrStar_AndNothingElse(string header, string expected)
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

    [Theory]
    [InlineData("\"a\", W/\"b\"", "\"a\":False;\"b\":True")]
    [InlineData("*", "*:False")]
    [InlineData("unquoted", "")]
    public async Task R8_IfMatchTags_ListEveryTag_WithItsWeakness(string header, string expected)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/if-match-tags");
        request.Headers.TryAddWithoutValidation(HeaderNames.IfMatch, header);

        using var response = await Client.SendAsync(request);

        (await response.Content.ReadAsStringAsync()).Should().Be(expected);
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

    private static Task<HttpResponseMessage> PutAsync(HttpClient client, string path, string? ifMatch)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, path);
        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation(HeaderNames.IfMatch, ifMatch);
        }

        return client.SendAsync(request);
    }

    public sealed record Order(string Id, EntityVersion Version);
}
