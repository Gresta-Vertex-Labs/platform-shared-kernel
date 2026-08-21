using System.Net;
using System.Net.Http.Json;
using System.Threading.RateLimiting;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Presentation.WebApi.RateLimiting;
using SharedKernel.ServiceDefaults.RateLimiting;

namespace SharedKernel.ServiceDefaults.Tests.RateLimiting;

/// <summary>
/// Per D-28 (WO-063/P-419) — a genuine, compiled proof of the corrected
/// <c>OnRejected</c> recipe documented in this domain's <c>README.md</c>: calling
/// <c>14.Presentation.WebApi</c>'s real <see cref="RateLimitRejectionProblemDetails"/> helper
/// instead of hand-rolling a raw <see cref="ProblemDetails"/> object. Uses a test-only
/// <c>ProjectReference</c> to <c>SharedKernel.Presentation.WebApi</c> (see this test project's
/// <c>.csproj</c>) — never referenced by the production <c>SharedKernel.ServiceDefaults.csproj</c>.
/// </summary>
public sealed class RateLimitRejectionRecipeTests
{
    private const string PolicyName = "single-permit";

    [Fact]
    public async Task AddSharedKernelRateLimiting_OnRejectedRecipeConfigured_RejectedRequestReturnsRateLimitRejectionProblemDetailsShapedBody()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddRouting();

        // Byte-identical to D-28's corrected README recipe.
        builder.AddSharedKernelRateLimiting(options =>
        {
            options.AddFixedWindowLimiter(PolicyName, policyOptions =>
            {
                policyOptions.PermitLimit = 1;
                policyOptions.Window = TimeSpan.FromMinutes(1);
                policyOptions.QueueLimit = 0;
            });

            options.OnRejected = async (context, cancellationToken) =>
            {
                var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfterMetadata)
                    ? retryAfterMetadata
                    : (TimeSpan?)null;

                var problemDetails = RateLimitRejectionProblemDetails.Create(context.HttpContext, retryAfter);

                await context.HttpContext.Response.WriteAsJsonAsync(
                    problemDetails,
                    options: null,
                    contentType: "application/problem+json",
                    cancellationToken: cancellationToken);
            };
        });

        await using var app = builder.Build();
        app.UseRouting();
        app.UseRateLimiter();
        app.MapGet("/limited", () => "ok").RequireRateLimiting(PolicyName);

        await app.StartAsync();
        using var client = app.GetTestClient();

        var first = await client.GetAsync("/limited");
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        var second = await client.GetAsync("/limited");

        second.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        second.Content.Headers.ContentType.Should().NotBeNull();
        second.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        var problemDetails = await second.Content.ReadFromJsonAsync<ProblemDetails>();

        problemDetails.Should().NotBeNull();
        problemDetails!.Status.Should().Be(StatusCodes.Status429TooManyRequests);
        problemDetails.Type.Should().NotBeNullOrWhiteSpace();
        problemDetails.Type.Should().StartWith("https://");
        problemDetails.Extensions.Should().ContainKey("traceId");
        problemDetails.Extensions["traceId"].Should().NotBeNull();

        second.Headers.TryGetValues("Retry-After", out var retryAfterValues).Should().BeTrue();
        var retryAfterValue = retryAfterValues!.Single();
        long.TryParse(retryAfterValue, out var retryAfterSeconds).Should().BeTrue();
        retryAfterSeconds.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task AddSharedKernelRateLimiting_NoOnRejectedConfigured_RejectedRequestIsBclDefault_EmptyBodyNoRetryAfterHeader()
    {
        // T-68 companion regression: the pre-existing, still-supported call shape (no OnRejected
        // override) must remain byte-identical to today — the D-28 recipe is additive, never a
        // forced default change.
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddRouting();

        builder.AddSharedKernelRateLimiting(options =>
        {
            options.AddFixedWindowLimiter(PolicyName, policyOptions =>
            {
                policyOptions.PermitLimit = 1;
                policyOptions.Window = TimeSpan.FromMinutes(1);
                policyOptions.QueueLimit = 0;
            });
        });

        await using var app = builder.Build();
        app.UseRouting();
        app.UseRateLimiter();
        app.MapGet("/limited", () => "ok").RequireRateLimiting(PolicyName);

        await app.StartAsync();
        using var client = app.GetTestClient();

        var first = await client.GetAsync("/limited");
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        var second = await client.GetAsync("/limited");

        second.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        var body = await second.Content.ReadAsStringAsync();
        body.Should().BeEmpty();
        second.Content.Headers.ContentType.Should().BeNull();
        second.Headers.TryGetValues("Retry-After", out _).Should().BeFalse();
    }
}
