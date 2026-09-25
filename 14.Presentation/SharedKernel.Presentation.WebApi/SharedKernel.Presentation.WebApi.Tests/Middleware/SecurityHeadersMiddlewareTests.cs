using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Presentation.WebApi.Middleware;
using Xunit;

namespace SharedKernel.Presentation.WebApi.Tests.Middleware;

public class SecurityHeadersMiddlewareTests
{
    [Fact]
    public async Task UseSharedKernelSecurityHeaders_NoConfiguration_WritesDocumentedDefaultHeaderSet()
    {
        var httpContext = CreateHttpContext(out var responseFeature);
        var pipeline = BuildPipeline(_ => Task.CompletedTask);

        await pipeline(httpContext);
        await responseFeature.FireOnStartingAsync();

        httpContext.Response.Headers["Strict-Transport-Security"].ToString()
            .Should().Be("max-age=31536000; includeSubDomains");
        httpContext.Response.Headers["X-Content-Type-Options"].ToString().Should().Be("nosniff");
        httpContext.Response.Headers["X-Frame-Options"].ToString().Should().Be("DENY");
        httpContext.Response.Headers["Referrer-Policy"].ToString().Should().Be("strict-origin-when-cross-origin");
        httpContext.Response.Headers["Permissions-Policy"].ToString()
            .Should().Be("geolocation=(), microphone=(), camera=()");
    }

    [Fact]
    public async Task UseSharedKernelSecurityHeaders_NoContentSecurityPolicyConfigured_NeverSetsCspHeader()
    {
        var httpContext = CreateHttpContext(out var responseFeature);
        var pipeline = BuildPipeline(_ => Task.CompletedTask);

        await pipeline(httpContext);
        await responseFeature.FireOnStartingAsync();

        httpContext.Response.Headers.ContainsKey("Content-Security-Policy").Should().BeFalse();
    }

    [Fact]
    public async Task UseSharedKernelSecurityHeaders_ContentSecurityPolicyConfigured_SetsConfiguredValue()
    {
        var httpContext = CreateHttpContext(out var responseFeature);
        var pipeline = BuildPipeline(
            _ => Task.CompletedTask,
            options => options.WithContentSecurityPolicy("default-src 'self'"));

        await pipeline(httpContext);
        await responseFeature.FireOnStartingAsync();

        httpContext.Response.Headers["Content-Security-Policy"].ToString().Should().Be("default-src 'self'");
    }

    [Fact]
    public async Task UseSharedKernelSecurityHeaders_HeaderAlreadySetByInnerMiddleware_NeverOverwritten()
    {
        var httpContext = CreateHttpContext(out var responseFeature);
        var pipeline = BuildPipeline(ctx =>
        {
            ctx.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
            return Task.CompletedTask;
        });

        await pipeline(httpContext);
        await responseFeature.FireOnStartingAsync();

        httpContext.Response.Headers["X-Frame-Options"].ToString().Should().Be("SAMEORIGIN");
    }

    [Fact]
    public async Task UseSharedKernelSecurityHeaders_HstsDisabledViaConfiguration_NoHstsHeader_OtherHeadersUnaffected()
    {
        var httpContext = CreateHttpContext(out var responseFeature);
        var pipeline = BuildPipeline(
            _ => Task.CompletedTask,
            options => options.Hsts.Enabled = false);

        await pipeline(httpContext);
        await responseFeature.FireOnStartingAsync();

        httpContext.Response.Headers.ContainsKey("Strict-Transport-Security").Should().BeFalse();
        httpContext.Response.Headers["X-Content-Type-Options"].ToString().Should().Be("nosniff");
        httpContext.Response.Headers["X-Frame-Options"].ToString().Should().Be("DENY");
    }

    private static RequestDelegate BuildPipeline(RequestDelegate terminal, Action<SecurityHeadersOptions>? configure = null)
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var appBuilder = new ApplicationBuilder(services);
        appBuilder.UseSharedKernelSecurityHeaders(configure);
        appBuilder.Run(terminal);
        return appBuilder.Build();
    }

    private static HttpContext CreateHttpContext(out FiringHttpResponseFeature responseFeature)
    {
        var httpContext = new DefaultHttpContext();
        responseFeature = new FiringHttpResponseFeature();
        httpContext.Features.Set<IHttpResponseFeature>(responseFeature);
        httpContext.Features.Set<IHttpResponseBodyFeature>(new StreamResponseBodyFeature(Stream.Null));
        return httpContext;
    }

    /// <summary>
    /// <see cref="DefaultHttpContext"/>'s default <see cref="IHttpResponseFeature"/> records
    /// <c>OnStarting</c> callbacks but never fires them outside a real server pipeline
    /// (Kestrel/TestServer). Test-local helper.
    /// </summary>
    private sealed class FiringHttpResponseFeature : HttpResponseFeature
    {
        private readonly List<(Func<object, Task> Callback, object State)> _onStartingCallbacks = [];

        public override void OnStarting(Func<object, Task> callback, object state)
            => _onStartingCallbacks.Add((callback, state));

        public async Task FireOnStartingAsync()
        {
            foreach (var (callback, state) in _onStartingCallbacks)
                await callback(state);
        }
    }
}
