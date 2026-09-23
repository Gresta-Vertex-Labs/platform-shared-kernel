using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using SharedKernel.Presentation.WebApi.Tests.TestSupport;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using Xunit;

namespace SharedKernel.Presentation.WebApi.Tests.Integration;

/// <summary>Design D1/D14/D16: outages are 503 (with the configured <c>Retry-After</c>) and timeouts 504, never 500.</summary>
public sealed class OutageTests
{
    [Fact]
    public async Task Unavailable_Is503_WithoutRetryAfter_ByDefault()
    {
        await using var app = await StartAsync(retryAfter: null);

        using var response = await app.GetTestClient().GetAsync("/unavailable");

        await response.ShouldBeProblemAsync(StatusCodes.Status503ServiceUnavailable, ErrorCodes.Unavailable.Default);
        response.Headers.RetryAfter.Should().BeNull();
    }

    [Fact]
    public async Task Unavailable_Is503_WithTheConfiguredRetryAfter_InWholeSeconds()
    {
        await using var app = await StartAsync(retryAfter: TimeSpan.FromSeconds(29.2));

        using var response = await app.GetTestClient().GetAsync("/unavailable");

        await response.ShouldBeProblemAsync(StatusCodes.Status503ServiceUnavailable, ErrorCodes.Unavailable.Default);
        response.Headers.RetryAfter!.Delta.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task Timeout_Is504_WithoutRetryAfter()
    {
        await using var app = await StartAsync(retryAfter: TimeSpan.FromSeconds(30));

        using var response = await app.GetTestClient().GetAsync("/timeout");

        await response.ShouldBeProblemAsync(StatusCodes.Status504GatewayTimeout, ErrorCodes.Timeout.Default);
        response.Headers.RetryAfter.Should().BeNull();
    }

    [Fact]
    public async Task TimeoutMessage_IsHiddenOutsideDevelopment()
    {
        await using var app = await StartAsync(retryAfter: null);

        using var response = await app.GetTestClient().GetAsync("/timeout");

        var problem = await response.ShouldBeProblemAsync(StatusCodes.Status504GatewayTimeout, ErrorCodes.Timeout.Default);
        problem.Detail().Should().Be("The operation did not complete in time.");
    }

    private static Task<WebApplication> StartAsync(TimeSpan? retryAfter) =>
        WebApiTestHost.StartAsync(
            app =>
            {
                app.MapGet("/unavailable", () => Result<string>.Failure(Error.Unavailable(ErrorCodes.Unavailable.Default, "Broker rabbit-1 refused.")).ToOk());
                app.MapGet("/timeout", () => Result<string>.Failure(Error.Timeout(ErrorCodes.Timeout.Default, "Query ran 30s.")).ToOk());
            },
            configureOptions: options => options.Problems.UnavailableRetryAfter = retryAfter);
}
