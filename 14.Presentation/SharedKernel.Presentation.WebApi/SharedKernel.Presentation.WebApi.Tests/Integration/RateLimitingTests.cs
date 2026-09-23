using System.Net;
using System.Threading.RateLimiting;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Presentation.WebApi.Errors;
using SharedKernel.Presentation.WebApi.RateLimiting;
using SharedKernel.Presentation.WebApi.Tests.TestSupport;
using SharedKernel.Primitives.Logging;
using SharedKernel.Testing.Logging;
using Xunit;

namespace SharedKernel.Presentation.WebApi.Tests.Integration;

/// <summary>
/// Design D8/D16: rate limiting configured without its own <c>OnRejected</c> answers 429 <c>rate_limit.exceeded</c>
/// with the limiter's <c>Retry-After</c>, and logs the rejection; a service's own <c>OnRejected</c> is kept.
/// </summary>
public sealed class RateLimitingTests
{
    private const string Policy = "one-per-window";

    [Fact]
    public async Task Rejection_Is429_WithRetryAfter_AndIsLogged()
    {
        var logs = new InMemoryLoggerFactory();
        await using var app = await StartAsync(onRejected: null, logs);
        var client = app.GetTestClient();

        using var first = await client.GetAsync("/limited");
        using var second = await client.GetAsync("/limited");

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        await second.ShouldBeProblemAsync(StatusCodes.Status429TooManyRequests, PresentationErrorCodes.RateLimitExceeded);
        second.Headers.RetryAfter!.Delta.Should().BePositive().And.BeLessThanOrEqualTo(TimeSpan.FromMinutes(10));
        logs.GetLogger(typeof(RateLimitRejectionPostConfigure).FullName!).Records
            .Should().Contain(record => record.EventId.Id == LoggingEventIdRanges.Presentation + 5);
    }

    [Fact]
    public async Task ServiceOwnOnRejected_IsKept()
    {
        await using var app = await StartAsync(
            onRejected: (context, _) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                return ValueTask.CompletedTask;
            },
            logs: null);
        var client = app.GetTestClient();

        using var first = await client.GetAsync("/limited");
        using var second = await client.GetAsync("/limited");

        // The service's status stands; being bodiless, it still gets the one problem shape from status-code pages.
        await second.ShouldBeProblemAsync(StatusCodes.Status503ServiceUnavailable, "http.503");
    }

    [Fact]
    public async Task ServiceOwnOnRejected_ThatWritesABody_IsUntouched()
    {
        await using var app = await StartAsync(
            onRejected: async (context, cancellationToken) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                await context.HttpContext.Response.WriteAsync("slow down", cancellationToken);
            },
            logs: null);
        var client = app.GetTestClient();

        using var first = await client.GetAsync("/limited");
        using var second = await client.GetAsync("/limited");

        second.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await second.Content.ReadAsStringAsync()).Should().Be("slow down");
    }

    [Fact]
    public async Task WithoutRateLimiting_NothingIsLimited()
    {
        await using var app = await WebApiTestHost.StartAsync(app => app.MapGet("/free", () => "ok"));
        var client = app.GetTestClient();

        for (var i = 0; i < 5; i++)
        {
            using var response = await client.GetAsync("/free");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    private static Task<WebApplication> StartAsync(
        Func<OnRejectedContext, CancellationToken, ValueTask>? onRejected,
        InMemoryLoggerFactory? logs) =>
        WebApiTestHost.StartAsync(
            app => app.MapGet("/limited", () => "ok").RequireRateLimiting(Policy),
            builder => builder.Services.AddRateLimiter(options =>
            {
                options.OnRejected = onRejected;
                options.AddFixedWindowLimiter(Policy, limiter =>
                {
                    limiter.PermitLimit = 1;
                    limiter.Window = TimeSpan.FromMinutes(10);
                    limiter.QueueLimit = 0;
                });
            }),
            loggerFactory: logs);
}
