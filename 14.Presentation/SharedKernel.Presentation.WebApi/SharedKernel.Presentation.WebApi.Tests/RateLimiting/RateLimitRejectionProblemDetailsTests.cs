using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;
using SharedKernel.Presentation.WebApi.RateLimiting;
using Xunit;

namespace SharedKernel.Presentation.WebApi.Tests.RateLimiting;

public class RateLimitRejectionProblemDetailsTests
{
    [Fact]
    public void Create_ProducesShape429WithRfc9457TypeUri()
    {
        var httpContext = new DefaultHttpContext();

        var problemDetails = RateLimitRejectionProblemDetails.Create(httpContext);

        problemDetails.Status.Should().Be(StatusCodes.Status429TooManyRequests);
        problemDetails.Type.Should().Be("https://httpstatuses.io/429");
    }

    [Fact]
    public void Create_PopulatesTraceIdIdenticallyToEveryOtherErrorPath()
    {
        var httpContext = new DefaultHttpContext { TraceIdentifier = "trace-429" };

        var problemDetails = RateLimitRejectionProblemDetails.Create(httpContext);

        problemDetails.Extensions["traceId"].Should().Be("trace-429");
    }

    [Fact]
    public void Create_RetryAfterSupplied_SetsRetryAfterResponseHeaderInWholeSeconds()
    {
        var httpContext = new DefaultHttpContext();

        RateLimitRejectionProblemDetails.Create(httpContext, TimeSpan.FromSeconds(30));

        httpContext.Response.Headers[HeaderNames.RetryAfter].ToString().Should().Be("30");
    }

    [Fact]
    public void Create_RetryAfterSuppliedWithFractionalSeconds_RoundsUp()
    {
        var httpContext = new DefaultHttpContext();

        RateLimitRejectionProblemDetails.Create(httpContext, TimeSpan.FromMilliseconds(1500));

        httpContext.Response.Headers[HeaderNames.RetryAfter].ToString().Should().Be("2");
    }

    [Fact]
    public void Create_RetryAfterNotSupplied_NoRetryAfterHeaderSet()
    {
        var httpContext = new DefaultHttpContext();

        RateLimitRejectionProblemDetails.Create(httpContext);

        httpContext.Response.Headers.ContainsKey(HeaderNames.RetryAfter).Should().BeFalse();
    }

    [Fact]
    public void Create_NullContext_Throws()
    {
        Action act = () => RateLimitRejectionProblemDetails.Create(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
