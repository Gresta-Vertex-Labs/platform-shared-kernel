using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Presentation.WebApi.Tests.TestSupport;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Logging;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Testing.Logging;
using Xunit;

namespace SharedKernel.Presentation.WebApi.Tests.Integration;

/// <summary>
/// Design D9/D16 with R6 and R18: a required <c>Idempotency-Key</c> is enforced by one middleware for every kind of
/// endpoint — the convention, the attribute on a minimal-API lambda, an <see cref="IdempotencyKey"/> parameter and an
/// MVC action — with 400 and a distinct code for a missing and a malformed key, the quoted IETF form accepted, the key
/// never logged, and the check made only after authorization.
/// </summary>
public sealed class IdempotencyKeyTests : IClassFixture<FullStackHost>
{
    public static TheoryData<string> Endpoints => new()
    {
        "/idempotent",
        "/idempotent-attribute",
        "/idempotent-parameter",
        "/mvc-api/idempotent",
    };

    private readonly FullStackHost _host;

    public IdempotencyKeyTests(FullStackHost host)
    {
        _host = host;
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task MissingKey_Is400_KeyRequired(string path)
    {
        using var response = await _host.Client.PostAsync(path, content: null);

        await response.ShouldBeProblemAsync(StatusCodes.Status400BadRequest, PresentationErrorCodes.IdempotencyKeyRequired);
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task BlankKey_IsMissing_Is400_KeyRequired(string path)
    {
        using var response = await SendAsync(path, "   ");

        await response.ShouldBeProblemAsync(StatusCodes.Status400BadRequest, PresentationErrorCodes.IdempotencyKeyRequired);
    }

    [Theory]
    [InlineData("/idempotent", "has space")]
    [InlineData("/idempotent", "\"\"")]
    [InlineData("/idempotent", "é-not-ascii")]
    [InlineData("/idempotent-attribute", "has space")]
    [InlineData("/idempotent-parameter", "has space")]
    [InlineData("/mvc-api/idempotent", "has space")]
    public async Task MalformedKey_Is400_KeyInvalid(string path, string key)
    {
        using var response = await SendAsync(path, key);

        await response.ShouldBeProblemAsync(StatusCodes.Status400BadRequest, PresentationErrorCodes.IdempotencyKeyInvalid);
    }

    [Fact]
    public async Task OverlongKey_Is400_KeyInvalid()
    {
        using var response = await SendAsync("/idempotent", new string('k', 257));

        await response.ShouldBeProblemAsync(StatusCodes.Status400BadRequest, PresentationErrorCodes.IdempotencyKeyInvalid);
    }

    [Fact]
    public async Task TwoKeys_Are400_KeyInvalid()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/idempotent");
        request.Headers.TryAddWithoutValidation(WellKnownHeaders.IdempotencyKey, ["key-1", "key-2"]);

        using var response = await _host.Client.SendAsync(request);

        await response.ShouldBeProblemAsync(StatusCodes.Status400BadRequest, PresentationErrorCodes.IdempotencyKeyInvalid);
    }

    [Theory]
    [InlineData("/idempotent", "8e03978e-40d5-43e8-bc93-6894a57f9324", "8e03978e-40d5-43e8-bc93-6894a57f9324")]
    [InlineData("/idempotent", "\"8e03978e-40d5-43e8-bc93-6894a57f9324\"", "8e03978e-40d5-43e8-bc93-6894a57f9324")]
    [InlineData("/idempotent-attribute", "order-17", "order-17")]
    [InlineData("/idempotent-parameter", "\"order-17\"", "order-17")]
    [InlineData("/mvc-api/idempotent", "\"order-17\"", "order-17")]
    public async Task ValidKey_ReachesTheHandler_WithoutQuotes(string path, string key, string expected)
    {
        using var response = await SendAsync(path, key);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Trim('"').Should().Be(expected);
    }

    [Fact]
    public async Task KeyOf256Characters_IsAccepted()
    {
        using var response = await SendAsync("/idempotent", new string('k', 256));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task R6_AnonymousCaller_IsToldToAuthenticate_BeforeTheHeaderIsChecked()
    {
        using var anonymous = await _host.Client.PostAsync("/idempotent-protected", content: null);
        using var signedIn = await _host.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Post, "/idempotent-protected").SignedIn(permissions: "orders.write"));

        await anonymous.ShouldBeProblemAsync(StatusCodes.Status401Unauthorized, ErrorCodes.Unauthorized.Default);
        await signedIn.ShouldBeProblemAsync(StatusCodes.Status400BadRequest, PresentationErrorCodes.IdempotencyKeyRequired);
    }

    [Fact]
    public async Task R6_KeyAndIfMatch_AreBothRequired_TheKeyFirst()
    {
        using var noKey = await _host.Client.PostAsync("/idempotent-and-versioned", content: null);
        using var noIfMatch = await SendAsync("/idempotent-and-versioned", "order-17");

        await noKey.ShouldBeProblemAsync(StatusCodes.Status400BadRequest, PresentationErrorCodes.IdempotencyKeyRequired);
        await noIfMatch.ShouldBeProblemAsync(StatusCodes.Status428PreconditionRequired, PresentationErrorCodes.PreconditionRequired);
    }

    [Fact]
    public async Task R6_GrpcCall_GetsTheStatus_WithoutABody()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/idempotent")
        {
            Content = new ByteArrayContent([]) { Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/grpc") } },
        };

        using var response = await _host.Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsByteArrayAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Rejection_IsLoggedAtWarning_WithoutTheKey()
    {
        const string SecretKey = "secret key with spaces";

        using var response = await SendAsync("/idempotent", SecretKey);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var logger = _host.Logs.GetLogger("SharedKernel.Presentation.WebApi.IdempotencyKeyGuard");
        var record = logger.Records.Last(r => r.EventId.Id == LoggingEventIdRanges.Presentation + 3);
        record.LogLevel.Should().Be(Microsoft.Extensions.Logging.LogLevel.Warning);
        record.Message.Should().Contain(PresentationErrorCodes.IdempotencyKeyInvalid).And.NotContain(SecretKey);
    }

    [Fact]
    public void GetIdempotencyKey_WithoutTheHeader_IsNull()
    {
        new DefaultHttpContext().GetIdempotencyKey().Should().BeNull();
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public void R18_EveryDeclaration_CarriesTheMetadataOpenApiReads(string route)
    {
        var endpoint = _host.App.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Single(candidate => "/" + candidate.RoutePattern.RawText!.TrimStart('/') == route);

        endpoint.Metadata.GetMetadata<IIdempotencyKeyRequiredMetadata>().Should().NotBeNull();
        endpoint.Metadata.GetMetadata<IIfMatchRequiredMetadata>().Should().BeNull();
    }

    [Fact]
    public void R6_AttributesAreMetadataOnly()
    {
        typeof(RequireIdempotencyKeyAttribute).GetInterfaces().Should().Equal(typeof(IIdempotencyKeyRequiredMetadata));
        typeof(RequireIfMatchAttribute).GetInterfaces().Should().Equal(typeof(IIfMatchRequiredMetadata));
        typeof(AcceptIdempotencyKeyAttribute).GetInterfaces().Should().Equal(typeof(IIdempotencyKeyAcceptedMetadata));
        typeof(AcceptIfMatchAttribute).GetInterfaces().Should().Equal(typeof(IIfMatchAcceptedMetadata));
    }

    [Fact]
    public void R18_IdempotencyKey_IsConstructibleInUnitTests_AndValidatesItsValue()
    {
        var key = new IdempotencyKey("order-17");

        key.Value.Should().Be("order-17");
        key.ToString().Should().Be("order-17");
        key.Should().Be(new IdempotencyKey("order-17"));
        FluentActions.Invoking(() => new IdempotencyKey("has space")).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new IdempotencyKey(new string('k', 257))).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new IdempotencyKey(string.Empty)).Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task J1_IdempotencyKeyBinding_WithoutThePipeline_BindsAMissingKeyAsNull_AndRefusesAnInvalidOne()
    {
        var valid = new DefaultHttpContext();
        valid.Request.Headers[WellKnownHeaders.IdempotencyKey] = "\"order-17\"";
        var missing = new DefaultHttpContext();
        var invalid = new DefaultHttpContext();
        invalid.Request.Headers[WellKnownHeaders.IdempotencyKey] = "has space";

        (await IdempotencyKey.BindAsync(valid)).Should().Be(new IdempotencyKey("order-17"));
        (await IdempotencyKey.BindAsync(missing)).Should().BeNull();

        // Without UseSharedKernelWebApi() nothing refuses the key before binding; null would read it as missing, which
        // a nullable parameter takes for "no idempotency", so a retry would run twice.
        var bindInvalid = async () => await IdempotencyKey.BindAsync(invalid);
        (await bindInvalid.Should().ThrowAsync<BadHttpRequestException>()).Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    private Task<HttpResponseMessage> SendAsync(string path, string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.TryAddWithoutValidation(WellKnownHeaders.IdempotencyKey, key);
        return _host.Client.SendAsync(request);
    }
}
