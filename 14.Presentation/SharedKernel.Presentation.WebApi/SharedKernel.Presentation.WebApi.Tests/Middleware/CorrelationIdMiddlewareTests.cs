using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Presentation.WebApi.Middleware;
using Xunit;

namespace SharedKernel.Presentation.WebApi.Tests.Middleware;

public class CorrelationIdMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_HeaderPresent_UsesProvidedValue()
    {
        const string providedCorrelationId = "client-correlation-id";
        var httpContext = CreateHttpContext(out var responseFeature);
        httpContext.Request.Headers[CorrelationIdMiddleware.HeaderName] = providedCorrelationId;

        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask, NullLogger<CorrelationIdMiddleware>.Instance);

        await middleware.InvokeAsync(httpContext);
        await responseFeature.FireOnStartingAsync();

        httpContext.Items[CorrelationIdMiddleware.ItemsKey].Should().Be(providedCorrelationId);
        httpContext.Response.Headers[CorrelationIdMiddleware.HeaderName].ToString().Should().Be(providedCorrelationId);
    }

    [Fact]
    public async Task InvokeAsync_HeaderAbsent_GeneratesNewValue()
    {
        var httpContext = CreateHttpContext(out var responseFeature);

        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask, NullLogger<CorrelationIdMiddleware>.Instance);

        await middleware.InvokeAsync(httpContext);
        await responseFeature.FireOnStartingAsync();

        var storedValue = httpContext.Items[CorrelationIdMiddleware.ItemsKey] as string;
        storedValue.Should().NotBeNullOrWhiteSpace();
        storedValue!.Length.Should().Be(32); // Guid "N" format — 32 hex chars, no dashes
        storedValue.Should().MatchRegex("^[0-9a-f]{32}$");
    }

    [Fact]
    public async Task InvokeAsync_HeaderWhitespace_GeneratesNewValue()
    {
        var httpContext = CreateHttpContext(out var responseFeature);
        httpContext.Request.Headers[CorrelationIdMiddleware.HeaderName] = "   ";

        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask, NullLogger<CorrelationIdMiddleware>.Instance);

        await middleware.InvokeAsync(httpContext);
        await responseFeature.FireOnStartingAsync();

        var storedValue = httpContext.Items[CorrelationIdMiddleware.ItemsKey] as string;
        storedValue.Should().NotBe("   ");
        storedValue!.Length.Should().Be(32);
    }

    [Fact]
    public async Task InvokeAsync_ResponseHeaderAlwaysSet_EvenWhenNextShortCircuits()
    {
        var httpContext = CreateHttpContext(out var responseFeature);

        // Simulate a short-circuited pipeline: next() sets a response status and returns without
        // throwing, exactly like the exception handler writing a terminal response.
        var middleware = new CorrelationIdMiddleware(
            ctx =>
            {
                ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
                return Task.CompletedTask;
            },
            NullLogger<CorrelationIdMiddleware>.Instance);

        await middleware.InvokeAsync(httpContext);
        await responseFeature.FireOnStartingAsync();

        httpContext.Response.Headers.ContainsKey(CorrelationIdMiddleware.HeaderName).Should().BeTrue();
    }

    /// <summary>
    /// <see cref="DefaultHttpContext"/>'s default <see cref="IHttpResponseFeature"/> records
    /// <c>OnStarting</c> callbacks but never fires them outside a real server pipeline
    /// (Kestrel/TestServer). This test-only feature exposes <see cref="FiringHttpResponseFeature.FireOnStartingAsync"/>
    /// so unit tests can verify the middleware's <c>OnStarting</c> registration fires as expected.
    /// </summary>
    private static HttpContext CreateHttpContext(out FiringHttpResponseFeature responseFeature)
    {
        var httpContext = new DefaultHttpContext();
        responseFeature = new FiringHttpResponseFeature();
        httpContext.Features.Set<IHttpResponseFeature>(responseFeature);
        httpContext.Features.Set<IHttpResponseBodyFeature>(new StreamResponseBodyFeature(Stream.Null));
        return httpContext;
    }

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
