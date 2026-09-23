using System.Net;
using System.Text.Json;
using System.Threading.RateLimiting;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using SharedKernel.Presentation.WebApi;
using SharedKernel.Presentation.WebApi.Errors;
using SharedKernel.ServiceDefaults.RateLimiting;

namespace SharedKernel.ServiceDefaults.Tests.RateLimiting;

/// <summary>
/// Proves the rate-limit recipe this domain documents: <c>AddSharedKernelRateLimiting()</c> together with
/// <c>14.Presentation</c>'s <c>AddSharedKernelWebApi()</c>/<c>UseSharedKernelWebApi()</c> answers a rejection with the
/// platform's RFC 9457 body — 429 <c>application/problem+json</c>, <c>errorCode</c> <c>rate_limit.exceeded</c> and
/// <c>Retry-After</c> when the limiter reports one — with no <c>OnRejected</c> written by the service and no
/// <c>UseRateLimiter()</c> call of its own.
/// </summary>
/// <remarks>
/// <para>
/// <c>SharedKernel.Presentation.WebApi</c>'s own tests prove the automatic body for a limiter registered with plain
/// <c>AddRateLimiter</c>. These prove the part only this domain can: that <c>AddSharedKernelRateLimiting()</c>'s
/// registration — its 429 status, global limiter, named policy and caller <c>configure</c> run last — leaves
/// <c>OnRejected</c> for that package to fill, and that <c>UseSharedKernelWebApi()</c> puts its limiter in the pipeline.
/// A default <c>OnRejected</c> added here would silently take the body away from every service using both.
/// </para>
/// <para>
/// The reference to <c>SharedKernel.Presentation.WebApi</c> is test-only (see this test project's <c>.csproj</c>); the
/// production <c>SharedKernel.ServiceDefaults.csproj</c> never references <c>14.Presentation</c>.
/// </para>
/// </remarks>
public sealed class RateLimitRejectionRecipeTests
{
    private const string PolicyName = "single-permit";

    private const string ProblemJson = "application/problem+json";

    [Fact]
    public async Task WithSharedKernelWebApi_PolicyRejection_Is429ProblemJson_WithRetryAfter()
    {
        await using var app = await StartWithWebApiAsync(options => options.AddFixedWindowLimiter(PolicyName, SinglePermit));
        using var client = app.GetTestClient();

        using var first = await client.GetAsync("/limited");
        using var second = await client.GetAsync("/limited");

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        await ShouldBeRateLimitProblemAsync(second);
    }

    [Fact]
    public async Task WithSharedKernelWebApi_GlobalLimiterRejection_GetsTheSameBody()
    {
        // The global limiter AddSharedKernelRateLimiting() installs, narrowed through configure so a burst of two
        // reaches it on an endpoint without a policy of its own.
        await using var app = await StartWithWebApiAsync(options =>
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
                _ => RateLimitPartition.GetFixedWindowLimiter("all", _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 1,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                })));
        using var client = app.GetTestClient();

        using var first = await client.GetAsync("/unlimited");
        using var second = await client.GetAsync("/unlimited");

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        await ShouldBeRateLimitProblemAsync(second);
    }

    [Fact]
    public async Task WithSharedKernelWebApi_ServiceOwnOnRejected_IsKept()
    {
        // configure runs last, so a service can still write its own rejection; the WebApi core fills OnRejected only
        // when nobody else did.
        await using var app = await StartWithWebApiAsync(options =>
        {
            options.AddFixedWindowLimiter(PolicyName, SinglePermit);
            options.OnRejected = async (context, cancellationToken) =>
                await context.HttpContext.Response.WriteAsync("slow down", cancellationToken);
        });
        using var client = app.GetTestClient();

        using var first = await client.GetAsync("/limited");
        using var second = await client.GetAsync("/limited");

        second.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await second.Content.ReadAsStringAsync()).Should().Be("slow down");
    }

    [Fact]
    public async Task WithoutSharedKernelWebApi_RejectionIsTheBclDefault_EmptyBodyNoRetryAfter()
    {
        // This package on its own changes nothing about the rejection: a bare 429, exactly as ASP.NET Core answers.
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.AddSharedKernelRateLimiting(options => options.AddFixedWindowLimiter(PolicyName, SinglePermit));

        await using var app = builder.Build();
        app.UseRouting();
        app.UseRateLimiter();
        app.MapGet("/limited", () => "ok").RequireRateLimiting(PolicyName);
        await app.StartAsync();
        using var client = app.GetTestClient();

        using var first = await client.GetAsync("/limited");
        using var second = await client.GetAsync("/limited");

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await second.Content.ReadAsStringAsync()).Should().BeEmpty();
        second.Content.Headers.ContentType.Should().BeNull();
        second.Headers.Contains("Retry-After").Should().BeFalse();
    }

    /// <summary>
    /// The documented composition: both registrations, then <c>UseSharedKernelWebApi()</c> and no
    /// <c>UseRateLimiter()</c> — the WebApi pipeline adds the limiter itself.
    /// </summary>
    private static async Task<WebApplication> StartWithWebApiAsync(Action<RateLimiterOptions> configureRateLimiting)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.AddSharedKernelWebApi();
        builder.AddSharedKernelRateLimiting(configureRateLimiting);

        var app = builder.Build();
        app.UseSharedKernelWebApi();
        app.MapGet("/limited", () => "ok").RequireRateLimiting(PolicyName);
        app.MapGet("/unlimited", () => "ok");

        await app.StartAsync();
        return app;
    }

    private static void SinglePermit(FixedWindowRateLimiterOptions limiter)
    {
        limiter.PermitLimit = 1;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
    }

    private static async Task ShouldBeRateLimitProblemAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests, body);
        response.Content.Headers.ContentType?.MediaType.Should().Be(ProblemJson);

        using var problem = JsonDocument.Parse(body);
        problem.RootElement.GetProperty("status").GetInt32().Should().Be(StatusCodes.Status429TooManyRequests);
        problem.RootElement.GetProperty(ProblemDetailsExtensionNames.ErrorCode).GetString()
            .Should().Be(PresentationErrorCodes.RateLimitExceeded);
        problem.RootElement.GetProperty(ProblemDetailsExtensionNames.TraceId).GetString().Should().NotBeNullOrWhiteSpace();

        // The fixed-window limiter reports how long until its window resets; the body's Retry-After carries it.
        response.Headers.RetryAfter.Should().NotBeNull();
        response.Headers.RetryAfter!.Delta.Should().BePositive().And.BeLessThanOrEqualTo(TimeSpan.FromMinutes(1));
    }
}
