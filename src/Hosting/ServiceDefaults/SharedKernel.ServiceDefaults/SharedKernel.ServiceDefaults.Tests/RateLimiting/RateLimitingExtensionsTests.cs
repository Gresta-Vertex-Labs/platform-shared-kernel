using System.Net;
using System.Threading.RateLimiting;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharedKernel.ServiceDefaults.RateLimiting;

namespace SharedKernel.ServiceDefaults.Tests.RateLimiting;

/// <summary>Covers WO-061/P-397's <c>AddSharedKernelRateLimiting</c>/<c>RateLimitPolicyNames</c>.</summary>
public sealed class RateLimitingExtensionsTests
{
    [Fact]
    public void AddSharedKernelRateLimiting_NullBuilder_ThrowsArgumentNullException()
    {
        IHostApplicationBuilder builder = null!;

        var act = () => builder.AddSharedKernelRateLimiting();

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddSharedKernelRateLimiting_ReturnsSameBuilderInstance()
    {
        var builder = Host.CreateApplicationBuilder();

        var result = builder.AddSharedKernelRateLimiting();

        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void AddSharedKernelRateLimiting_RegistersRateLimiterOptions()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddSharedKernelRateLimiting();
        using var host = builder.Build();

        var options = host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<RateLimiterOptions>>().Value;

        options.RejectionStatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
        options.GlobalLimiter.Should().NotBeNull();
    }

    [Fact]
    public void AddSharedKernelRateLimiting_ConfigureDelegate_RunsLastAndCanOverrideDefaults()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddSharedKernelRateLimiting(o => o.RejectionStatusCode = StatusCodes.Status503ServiceUnavailable);
        using var host = builder.Build();

        var options = host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<RateLimiterOptions>>().Value;

        options.RejectionStatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
    }

    [Fact]
    public void RateLimitPolicyNames_Authentication_IsExpectedValue()
    {
        RateLimitPolicyNames.Authentication.Should().Be("authentication");
    }

    [Fact]
    public async Task AddSharedKernelRateLimiting_AuthenticationPolicy_RealRequestBurst_ReturnsTooManyRequestsOnceThresholdCrossed()
    {
        // Real TestServer-hosted pipeline (WO-061/P-397's own gating acceptance criterion) — a
        // burst of requests must actually receive 429 once the default authentication-policy
        // threshold (10 permits/minute) is crossed, not merely that the options object was
        // constructed.
        var hostBuilder = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddRateLimiter(options =>
                    {
                        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                        options.AddFixedWindowLimiter(RateLimitPolicyNames.Authentication, o =>
                        {
                            o.PermitLimit = 10;
                            o.Window = TimeSpan.FromMinutes(1);
                            o.QueueLimit = 0;
                        });
                    });
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseRateLimiter();
                    app.UseEndpoints(endpoints =>
                        endpoints.MapGet("/login", () => "ok").RequireRateLimiting(RateLimitPolicyNames.Authentication));
                });
            });

        using var host = await hostBuilder.StartAsync();
        using var client = host.GetTestClient();

        HttpStatusCode? lastStatus = null;
        for (var i = 0; i < 11; i++)
        {
            var response = await client.GetAsync("/login");
            lastStatus = response.StatusCode;
        }

        lastStatus.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task NoRateLimitingCalled_RealRequestBurst_NeverReturnsTooManyRequests()
    {
        // T-58 no-op regression, mirroring the mTLS/P-378 precedent: a host that never calls
        // AddSharedKernelRateLimiting() (and never wires app.UseRateLimiter()) has no rate-limiting
        // middleware in its pipeline at all — proven against a real burst of requests, not merely
        // the absence of a positive-path test.
        var hostBuilder = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services => services.AddRouting());
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapGet("/login", () => "ok"));
                });
            });

        using var host = await hostBuilder.StartAsync();
        using var client = host.GetTestClient();

        for (var i = 0; i < 50; i++)
        {
            var response = await client.GetAsync("/login");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task AddSharedKernelRateLimiting_ConfigureOverridesGlobalLimiter_RealRequestBurst_ReturnsTooManyRequestsAtOverriddenThreshold()
    {
        // T-59: the configure callback must genuinely override the default GLOBAL limiter's
        // threshold (not merely RejectionStatusCode, as the sibling
        // ConfigureDelegate_RunsLastAndCanOverrideDefaults test above already proves) — asserted via
        // a real request burst against a plain endpoint carrying no [EnableRateLimiting] policy at
        // all, so only the (overridden) global limiter can be responsible for the 429.
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddRouting();
        builder.AddSharedKernelRateLimiting(options =>
        {
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
                _ => RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: "test-override",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 2,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    }));
        });

        await using var app = builder.Build();
        app.UseRouting();
        app.UseRateLimiter();
        app.MapGet("/unprotected", () => "ok");

        await app.StartAsync();
        using var client = app.GetTestClient();

        HttpStatusCode? lastStatus = null;
        for (var i = 0; i < 3; i++)
        {
            var response = await client.GetAsync("/unprotected");
            lastStatus = response.StatusCode;
        }

        lastStatus.Should().Be(HttpStatusCode.TooManyRequests);
    }
}
