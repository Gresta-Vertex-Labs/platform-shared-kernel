using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedKernel.Presentation.WebApi.Tests.TestSupport;
using SharedKernel.Primitives.Logging;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Testing.Logging;
using Xunit;
using HeaderNames = Microsoft.Net.Http.Headers.HeaderNames;

namespace SharedKernel.Presentation.WebApi.Tests.Integration;

/// <summary>
/// P-562 J1: an endpoint that accepts <c>Idempotency-Key</c> or <c>If-Match</c> without requiring it — through the
/// <c>Accept…()</c> conventions, the <c>[Accept…]</c> attributes on a minimal-API lambda or an MVC action, or a nullable
/// <see cref="IdempotencyKey"/> or <see cref="IfMatch{TVersion}"/> parameter — lets a request leave the header out, but
/// refuses a header it cannot use before the handler runs, so such a header is never read as a missing one. A
/// requirement on the same endpoint wins, and a parameter whose nullability cannot be told requires its header.
/// </summary>
public sealed class OptionalHeaderTests : IClassFixture<FullStackHost>
{
    private const string CurrentTag = "\"" + TestVersions.Current + "\"";

    private const string Key = "order-17";

    private static readonly string[] KeyPaths =
    [
        "/idempotent-optional",
        "/idempotent-optional-attribute",
        "/idempotent-optional-parameter",
        "/mvc-api/idempotent-optional",
    ];

    private static readonly string[] IfMatchPaths =
    [
        "/versioned-optional",
        "/versioned-optional-attribute",
        "/versioned-optional-parameter",
        "/mvc-api/versioned-optional",
    ];

    private readonly FullStackHost _host;

    public OptionalHeaderTests(FullStackHost host)
    {
        _host = host;
    }

    public static TheoryData<string> KeyEndpoints => new(KeyPaths);

    public static TheoryData<string> IfMatchEndpoints => new(IfMatchPaths);

    /// <summary>Every key endpoint with a key sent plain and quoted.</summary>
    public static TheoryData<string, string> ValidKeys => Combine(KeyPaths, Key, $"\"{Key}\"");

    /// <summary>Every key endpoint with every key the core refuses.</summary>
    public static TheoryData<string, string> InvalidKeys =>
        Combine(KeyPaths, "has space", "\"\"", "é-not-ascii", new string('k', IdempotencyKey.MaxLength + 1));

    /// <summary>Every If-Match endpoint with every header the core refuses, and the status and code it refuses it with.</summary>
    public static TheoryData<string, string, int, string> UnusableIfMatches
    {
        get
        {
            (string Header, int Status, string Code)[] refusals =
            [
                ("42", StatusCodes.Status400BadRequest, PresentationErrorCodes.PreconditionInvalid), // not quoted
                ("\"42", StatusCodes.Status400BadRequest, PresentationErrorCodes.PreconditionInvalid),
                ("\"41\", \"42\"", StatusCodes.Status400BadRequest, PresentationErrorCodes.PreconditionInvalid),
                ("*", StatusCodes.Status400BadRequest, PresentationErrorCodes.PreconditionInvalid), // names no version
                ("*, \"42\"", StatusCodes.Status400BadRequest, PresentationErrorCodes.PreconditionInvalid),
                ("W/" + CurrentTag, StatusCodes.Status412PreconditionFailed, PresentationErrorCodes.PreconditionFailed),
            ];

            var data = new TheoryData<string, string, int, string>();
            foreach (var path in IfMatchPaths)
            {
                foreach (var (header, status, code) in refusals)
                {
                    data.Add(path, header, status, code);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(KeyEndpoints))]
    public async Task MissingKey_ReachesTheHandler_AsNone(string path)
    {
        var before = _host.Calls.Count(path);

        using var response = await _host.Client.PostAsync(path, content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await BodyAsync(response)).Should().Be(HandlerCalls.None);
        _host.Calls.Count(path).Should().Be(before + 1);
    }

    [Theory]
    [MemberData(nameof(KeyEndpoints))]
    public async Task BlankKey_IsMissing_AsIfMatch(string path)
    {
        using var response = await SendKeyAsync(path, "   ");

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await BodyAsync(response)).Should().Be(HandlerCalls.None);
    }

    [Theory]
    [MemberData(nameof(ValidKeys))]
    public async Task ValidKey_ReachesTheHandler_WithoutQuotes(string path, string key)
    {
        using var response = await SendKeyAsync(path, key);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await BodyAsync(response)).Should().Be(Key);
    }

    [Theory]
    [MemberData(nameof(InvalidKeys))]
    public async Task InvalidKey_Is400_KeyInvalid_BeforeTheHandler(string path, string key)
    {
        var before = _host.Calls.Count(path);

        using var response = await SendKeyAsync(path, key);

        await response.ShouldBeProblemAsync(StatusCodes.Status400BadRequest, PresentationErrorCodes.IdempotencyKeyInvalid);
        _host.Calls.Count(path).Should().Be(before, "an invalid key never reaches the handler as a missing one");
    }

    [Theory]
    [MemberData(nameof(KeyEndpoints))]
    public async Task TwoKeys_Are400_KeyInvalid_BeforeTheHandler(string path)
    {
        var before = _host.Calls.Count(path);
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.TryAddWithoutValidation(WellKnownHeaders.IdempotencyKey, ["key-1", "key-2"]);

        using var response = await _host.Client.SendAsync(request);

        await response.ShouldBeProblemAsync(StatusCodes.Status400BadRequest, PresentationErrorCodes.IdempotencyKeyInvalid);
        _host.Calls.Count(path).Should().Be(before);
    }

    [Theory]
    [MemberData(nameof(IfMatchEndpoints))]
    public async Task MissingIfMatch_ReachesTheHandler_AsNone_Unconditional(string path)
    {
        var before = _host.Calls.Count(path);

        using var response = await PutAsync(path, ifMatch: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await BodyAsync(response)).Should().Be(HandlerCalls.None);
        _host.Calls.Count(path).Should().Be(before + 1);
    }

    [Theory]
    [MemberData(nameof(IfMatchEndpoints))]
    public async Task EmptyIfMatch_IsMissing_AsEverywhereInThePackage(string path)
    {
        using var response = await PutAsync(path, string.Empty);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await BodyAsync(response)).Should().Be(HandlerCalls.None);
    }

    [Theory]
    [MemberData(nameof(IfMatchEndpoints))]
    public async Task OneStrongTag_ReachesTheHandler_WithoutQuotes(string path)
    {
        using var response = await PutAsync(path, CurrentTag);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await BodyAsync(response)).Should().Be(TestVersions.Current);
    }

    [Theory]
    [MemberData(nameof(UnusableIfMatches))]
    public async Task UnusableIfMatch_IsRefused_BeforeTheHandler(string path, string ifMatch, int status, string code)
    {
        var before = _host.Calls.Count(path);

        using var response = await PutAsync(path, ifMatch);

        await response.ShouldBeProblemAsync(status, code);
        _host.Calls.Count(path).Should().Be(before, "an unusable If-Match never reaches the handler as a missing one");
    }

    [Fact]
    public async Task IfMatchParameter_RefusesATagThatIsNotAVersion_With412_BeforeTheHandler()
    {
        const string Path = "/versioned-optional-parameter";
        var before = _host.Calls.Count(Path);

        using var response = await PutAsync(Path, "\"not-a-version\"");

        await response.ShouldBeProblemAsync(StatusCodes.Status412PreconditionFailed, PresentationErrorCodes.PreconditionFailed);
        _host.Calls.Count(Path).Should().Be(before);
    }

    [Fact]
    public async Task Requirement_WinsOverAnAcceptingParameter()
    {
        var keyCalls = _host.Calls.Count("/idempotent-required-wins");
        var ifMatchCalls = _host.Calls.Count("/versioned-required-wins");

        using var noKey = await _host.Client.PostAsync("/idempotent-required-wins", content: null);
        using var noIfMatch = await PutAsync("/versioned-required-wins", ifMatch: null);
        using var star = await PutAsync("/versioned-required-wins", "*");
        using var withKey = await SendKeyAsync("/idempotent-required-wins", Key);
        using var withIfMatch = await PutAsync("/versioned-required-wins", CurrentTag);

        await noKey.ShouldBeProblemAsync(StatusCodes.Status400BadRequest, PresentationErrorCodes.IdempotencyKeyRequired);
        await noIfMatch.ShouldBeProblemAsync(StatusCodes.Status428PreconditionRequired, PresentationErrorCodes.PreconditionRequired);
        await star.ShouldBeProblemAsync(StatusCodes.Status428PreconditionRequired, PresentationErrorCodes.PreconditionRequired);
        (await BodyAsync(withKey)).Should().Be(Key);
        (await BodyAsync(withIfMatch)).Should().Be(TestVersions.Current);
        _host.Calls.Count("/idempotent-required-wins").Should().Be(keyCalls + 1, "only the request with the key ran");
        _host.Calls.Count("/versioned-required-wins").Should().Be(ifMatchCalls + 1, "only the request with a version ran");
    }

    [Fact]
    public async Task ParametersOfUnknownNullability_RequireTheirHeaders()
    {
        var keyCalls = _host.Calls.Count(ObliviousEndpoints.IdempotencyKeyPath);
        var ifMatchCalls = _host.Calls.Count(ObliviousEndpoints.IfMatchPath);

        using var noKey = await _host.Client.PostAsync(ObliviousEndpoints.IdempotencyKeyPath, content: null);
        using var noIfMatch = await PutAsync(ObliviousEndpoints.IfMatchPath, ifMatch: null);
        using var withKey = await SendKeyAsync(ObliviousEndpoints.IdempotencyKeyPath, Key);
        using var withIfMatch = await PutAsync(ObliviousEndpoints.IfMatchPath, CurrentTag);

        await noKey.ShouldBeProblemAsync(StatusCodes.Status400BadRequest, PresentationErrorCodes.IdempotencyKeyRequired);
        await noIfMatch.ShouldBeProblemAsync(StatusCodes.Status428PreconditionRequired, PresentationErrorCodes.PreconditionRequired);
        (await BodyAsync(withKey)).Should().Be(Key);
        (await BodyAsync(withIfMatch)).Should().Be(TestVersions.Current);
        _host.Calls.Count(ObliviousEndpoints.IdempotencyKeyPath).Should().Be(keyCalls + 1);
        _host.Calls.Count(ObliviousEndpoints.IfMatchPath).Should().Be(ifMatchCalls + 1);
    }

    [Theory]
    [InlineData("/idempotent-optional", typeof(IIdempotencyKeyAcceptedMetadata), typeof(IIdempotencyKeyRequiredMetadata))]
    [InlineData("/idempotent-optional-attribute", typeof(IIdempotencyKeyAcceptedMetadata), typeof(IIdempotencyKeyRequiredMetadata))]
    [InlineData("/idempotent-optional-parameter", typeof(IIdempotencyKeyAcceptedMetadata), typeof(IIdempotencyKeyRequiredMetadata))]
    [InlineData("/mvc-api/idempotent-optional", typeof(IIdempotencyKeyAcceptedMetadata), typeof(IIdempotencyKeyRequiredMetadata))]
    [InlineData("/versioned-optional", typeof(IIfMatchAcceptedMetadata), typeof(IIfMatchRequiredMetadata))]
    [InlineData("/versioned-optional-attribute", typeof(IIfMatchAcceptedMetadata), typeof(IIfMatchRequiredMetadata))]
    [InlineData("/versioned-optional-parameter", typeof(IIfMatchAcceptedMetadata), typeof(IIfMatchRequiredMetadata))]
    [InlineData("/mvc-api/versioned-optional", typeof(IIfMatchAcceptedMetadata), typeof(IIfMatchRequiredMetadata))]
    [InlineData(ObliviousEndpoints.IdempotencyKeyPath, typeof(IIdempotencyKeyRequiredMetadata), typeof(IIdempotencyKeyAcceptedMetadata))]
    [InlineData(ObliviousEndpoints.IfMatchPath, typeof(IIfMatchRequiredMetadata), typeof(IIfMatchAcceptedMetadata))]
    public void EveryDeclaration_CarriesTheMetadataOpenApiReads(string route, Type declared, Type notDeclared)
    {
        var metadata = _host.App.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Single(candidate => "/" + candidate.RoutePattern.RawText!.TrimStart('/') == route)
            .Metadata;

        metadata.Should().Contain(item => declared.IsInstanceOfType(item));
        metadata.Should().NotContain(item => notDeclared.IsInstanceOfType(item));
    }

    [Theory]
    [InlineData("/idempotent-optional", "has space", HttpStatusCode.BadRequest)]
    [InlineData("/versioned-optional", "*", HttpStatusCode.BadRequest)]
    [InlineData("/versioned-optional", "W/" + CurrentTag, HttpStatusCode.PreconditionFailed)]
    public async Task GrpcCall_GetsTheStatus_WithoutABody(string path, string value, HttpStatusCode expected)
    {
        var sendsKey = path == "/idempotent-optional";
        var before = _host.Calls.Count(path);
        using var request = new HttpRequestMessage(sendsKey ? HttpMethod.Post : HttpMethod.Put, path)
        {
            Content = new ByteArrayContent([]) { Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/grpc") } },
        };
        request.Headers.TryAddWithoutValidation(sendsKey ? WellKnownHeaders.IdempotencyKey : HeaderNames.IfMatch, value);

        using var response = await _host.Client.SendAsync(request);

        response.StatusCode.Should().Be(expected);
        (await response.Content.ReadAsByteArrayAsync()).Should().BeEmpty();
        _host.Calls.Count(path).Should().Be(before);
    }

    [Fact]
    public async Task InvalidKey_IsLoggedAtWarning_WithoutTheKey_AndAMissingOneIsNotLogged()
    {
        const string SecretKey = "optional secret key";
        var logger = _host.Logs.GetLogger("SharedKernel.Presentation.WebApi.IdempotencyKeyGuard");

        using (var missing = await _host.Client.PostAsync("/idempotent-optional", content: null))
        {
            missing.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var rejectionsBefore = Rejections().Length;

        using var invalid = await SendKeyAsync("/idempotent-optional", SecretKey);
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var rejections = Rejections();
        rejections.Should().HaveCount(rejectionsBefore + 1, "the missing key was not a rejection");
        rejections[^1].LogLevel.Should().Be(LogLevel.Warning);
        rejections[^1].Message.Should().Contain(PresentationErrorCodes.IdempotencyKeyInvalid).And.NotContain(SecretKey);

        LogRecord[] Rejections() =>
            [.. logger.Records.Where(record => record.EventId.Id == LoggingEventIdRanges.Presentation + 3)];
    }

    private static TheoryData<string, string> Combine(string[] paths, params string[] values)
    {
        var data = new TheoryData<string, string>();
        foreach (var path in paths)
        {
            foreach (var value in values)
            {
                data.Add(path, value);
            }
        }

        return data;
    }

    private static async Task<string> BodyAsync(HttpResponseMessage response) =>
        (await response.Content.ReadAsStringAsync()).Trim('"');

    private Task<HttpResponseMessage> SendKeyAsync(string path, string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.TryAddWithoutValidation(WellKnownHeaders.IdempotencyKey, key);
        return _host.Client.SendAsync(request);
    }

    private Task<HttpResponseMessage> PutAsync(string path, string? ifMatch)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, path);
        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation(HeaderNames.IfMatch, ifMatch);
        }

        return _host.Client.SendAsync(request);
    }
}
