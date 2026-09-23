using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using SharedKernel.Presentation.WebApi.Errors;
using SharedKernel.Presentation.WebApi.Idempotency;
using SharedKernel.Presentation.WebApi.Tests.TestSupport;
using SharedKernel.Primitives.Logging;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Testing.Logging;
using Xunit;

namespace SharedKernel.Presentation.WebApi.Tests.Integration;

/// <summary>
/// Design D9/D16: a required <c>Idempotency-Key</c> — one declaration for minimal APIs and MVC, 400 with a distinct code
/// for a missing and a malformed key, the quoted IETF form accepted, and the key never logged.
/// </summary>
public sealed class IdempotencyKeyTests : IClassFixture<FullStackHost>
{
    private readonly FullStackHost _host;

    public IdempotencyKeyTests(FullStackHost host)
    {
        _host = host;
    }

    [Theory]
    [InlineData("/idempotent")]
    [InlineData("/mvc-api/idempotent")]
    public async Task MissingKey_Is400_KeyRequired(string path)
    {
        using var response = await _host.Client.PostAsync(path, content: null);

        await response.ShouldBeProblemAsync(StatusCodes.Status400BadRequest, PresentationErrorCodes.IdempotencyKeyRequired);
    }

    [Theory]
    [InlineData("/idempotent", "has space")]
    [InlineData("/idempotent", "\"\"")]
    [InlineData("/idempotent", "é-not-ascii")]
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
    public async Task Rejection_IsLoggedAtWarning_WithoutTheKey()
    {
        const string SecretKey = "secret key with spaces";

        using var response = await SendAsync("/idempotent", SecretKey);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var logger = _host.Logs.GetLogger("SharedKernel.Presentation.WebApi.Idempotency.IdempotencyKeyGuard");
        var record = logger.Records.Last(r => r.EventId.Id == LoggingEventIdRanges.Presentation + 3);
        record.LogLevel.Should().Be(Microsoft.Extensions.Logging.LogLevel.Warning);
        record.Message.Should().Contain(PresentationErrorCodes.IdempotencyKeyInvalid).And.NotContain(SecretKey);
    }

    [Fact]
    public void GetIdempotencyKey_WithoutTheHeader_IsNull()
    {
        new DefaultHttpContext().GetIdempotencyKey().Should().BeNull();
    }

    [Fact]
    public void RequireIdempotencyKeyAttribute_IsTheOpenApiMarker()
    {
        new RequireIdempotencyKeyAttribute().Should().BeAssignableTo<IIdempotencyKeyRequiredMetadata>();
    }

    private Task<HttpResponseMessage> SendAsync(string path, string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.TryAddWithoutValidation(WellKnownHeaders.IdempotencyKey, key);
        return _host.Client.SendAsync(request);
    }
}
