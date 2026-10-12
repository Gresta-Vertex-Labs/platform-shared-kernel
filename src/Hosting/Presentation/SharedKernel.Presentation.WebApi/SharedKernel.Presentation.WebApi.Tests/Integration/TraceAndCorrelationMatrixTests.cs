using System.Diagnostics;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using SharedKernel.Presentation.WebApi.Tests.TestSupport;
using SharedKernel.Primitives.Errors;
using SharedKernel.Testing.Telemetry;
using Xunit;

namespace SharedKernel.Presentation.WebApi.Tests.Integration;

/// <summary>
/// SK.14.EdgeErrorContext T-09: every kind of HTTP error the platform produces carries a <c>traceId</c> that is the
/// W3C trace of the request and a <c>correlationId</c> equal to the response header (<c>ShouldBeProblemAsync</c>). The
/// other suites assert the same members on their own paths (413 in <c>KestrelLimitsTests</c>, paging in
/// <c>PagingTests</c>, the 429 body in <c>RateLimitingTests</c>, every authorization refusal in
/// <c>AuthorizationTests</c>); this matrix adds that the trace is the request's own.
/// </summary>
public sealed class TraceAndCorrelationMatrixTests : IClassFixture<FullStackHost>
{
    private readonly FullStackHost _host;

    public TraceAndCorrelationMatrixTests(FullStackHost host)
    {
        _host = host;
    }

    public static TheoryData<string, string, string?, int, string> Cases => new()
    {
        { "GET", "/no-such-route", null, StatusCodes.Status404NotFound, "http.404" },
        { "POST", "/only-get", null, StatusCodes.Status405MethodNotAllowed, "http.405" },
        { "GET", "/numbers?id=abc", null, StatusCodes.Status400BadRequest, ErrorCodes.Validation.InvalidFormat },
        { "GET", "/result-failure", null, StatusCodes.Status404NotFound, "order.not_found" },
        { "GET", "/throw-known", null, StatusCodes.Status404NotFound, "order.not_found" },
        { "GET", "/throw-unknown", null, StatusCodes.Status500InternalServerError, ErrorCodes.Unexpected.Default },
        { "GET", "/auth/perm", null, StatusCodes.Status401Unauthorized, ErrorCodes.Unauthorized.Default },
        { "GET", "/auth/perm", "signed-in", StatusCodes.Status403Forbidden, ErrorCodes.Forbidden.InsufficientPermission },
        { "GET", "/auth/fresh", "signed-in", StatusCodes.Status401Unauthorized, PresentationErrorCodes.StepUpRequired },
        { "POST", "/idempotent", "signed-in", StatusCodes.Status400BadRequest, PresentationErrorCodes.IdempotencyKeyRequired },
        { "PUT", "/versioned", "signed-in", StatusCodes.Status428PreconditionRequired, PresentationErrorCodes.PreconditionRequired },
        { "PUT", "/versioned", "stale", StatusCodes.Status412PreconditionFailed, TestErrors.ConcurrencyConflictCode },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task EveryErrorPath_CarriesTheRequestsTrace_AndItsCorrelationId(
        string method,
        string path,
        string? caller,
        int status,
        string errorCode)
    {
        using var recorder = ServerSpans.Record();
        var traceId = ActivityTraceId.CreateRandom();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        request.Headers.Add("traceparent", $"00-{traceId}-{ActivitySpanId.CreateRandom()}-01");

        if (caller is not null)
        {
            request.SignedIn();
        }

        if (caller == "stale")
        {
            request.Headers.TryAddWithoutValidation("If-Match", "\"" + TestVersions.Stale + "\"");
        }

        using var response = await _host.App.GetTestClient().SendAsync(request);

        var problem = await response.ShouldBeProblemAsync(status, errorCode);
        problem.GetProperty(ProblemDetailsExtensionNames.TraceId).GetString().Should().Contain(traceId.ToString());
    }

    [Fact]
    public async Task ARateLimitedRequest_CarriesTheRequestsTrace_AndItsCorrelationId()
    {
        using var recorder = ServerSpans.Record();
        var partition = Guid.NewGuid().ToString("N");
        var traceId = ActivityTraceId.CreateRandom();

        using var first = await SendLimitedAsync(partition, traceId);
        using var second = await SendLimitedAsync(partition, traceId);

        first.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var problem = await second.ShouldBeProblemAsync(StatusCodes.Status429TooManyRequests, PresentationErrorCodes.RateLimitExceeded);
        problem.GetProperty(ProblemDetailsExtensionNames.TraceId).GetString().Should().Contain(traceId.ToString());
    }

    private Task<HttpResponseMessage> SendLimitedAsync(string partition, ActivityTraceId traceId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/limited");
        request.Headers.Add("traceparent", $"00-{traceId}-{ActivitySpanId.CreateRandom()}-01");
        request.Headers.Add(FullStackHost.PartitionHeader, partition);
        return _host.App.GetTestClient().SendAsync(request);
    }
}
