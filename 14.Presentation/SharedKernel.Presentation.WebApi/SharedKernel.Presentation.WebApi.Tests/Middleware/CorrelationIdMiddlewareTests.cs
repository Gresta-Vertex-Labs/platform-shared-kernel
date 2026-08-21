using System.Diagnostics;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Presentation.WebApi.Middleware;
using SharedKernel.Primitives.Propagation;
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
    public void BaggageKey_EqualsExpectedLiteral()
    {
        // Locks the contract value itself (WO-041, P-256), not merely its existence — cross-domain
        // consumers (13.ServiceDefaults's BaggageLogRecordProcessor test suite, any future
        // consumer) must reference this constant rather than hand-copying a literal.
        CorrelationIdMiddleware.BaggageKey.Should().Be("correlation.id");
    }

    [Fact]
    public void HeaderName_ForwardsWellKnownHeadersCorrelationId()
    {
        // WO-042, P-262, T-14: proves byte-identical sourcing from 01.Core by direct reference to
        // the shared constant — not a hand-copied literal on either side of the assertion.
        CorrelationIdMiddleware.HeaderName.Should().Be(WellKnownHeaders.CorrelationId);
    }

    [Fact]
    public void BaggageKey_ForwardsWellKnownBaggageKeysCorrelationId()
    {
        // WO-042, P-262, T-14: proves byte-identical sourcing from 01.Core by direct reference to
        // the shared constant — not a hand-copied literal on either side of the assertion.
        CorrelationIdMiddleware.BaggageKey.Should().Be(WellKnownBaggageKeys.CorrelationId);
    }

    [Fact]
    public void ItemsKey_UnchangedFromPreP262Literal()
    {
        // Regression guard (WO-042, P-262, T-14): ItemsKey is presentation-local and explicitly
        // out of scope for the 01.Core forwarding-alias sourcing rule — its value must remain
        // exactly what it was before this phase.
        CorrelationIdMiddleware.ItemsKey.Should().Be("CorrelationId");
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

    // --- Caller-supplied correlation-id format validation (WO-063, P-415, T-55/T-56/T-57) ---

    [Theory]
    [InlineData("has spaces")]
    [InlineData("has/slash")]
    [InlineData("has<angle>brackets")]
    [InlineData("has\"quote")]
    public async Task InvokeAsync_HeaderContainsDisallowedCharacter_GeneratesFreshValueInstead(string invalidValue)
    {
        var httpContext = CreateHttpContext(out var responseFeature);
        httpContext.Request.Headers[CorrelationIdMiddleware.HeaderName] = invalidValue;

        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask, NullLogger<CorrelationIdMiddleware>.Instance);

        await middleware.InvokeAsync(httpContext);
        await responseFeature.FireOnStartingAsync();

        var storedValue = httpContext.Items[CorrelationIdMiddleware.ItemsKey] as string;
        storedValue.Should().NotBe(invalidValue);
        storedValue.Should().MatchRegex("^[0-9a-f]{32}$");
        httpContext.Response.Headers[CorrelationIdMiddleware.HeaderName].ToString().Should().Be(storedValue);
    }

    [Fact]
    public async Task InvokeAsync_HeaderExceedsMaxLength_GeneratesFreshValueInstead()
    {
        var httpContext = CreateHttpContext(out var responseFeature);
        var overLength = new string('a', 129); // MaxLength default is 128.
        httpContext.Request.Headers[CorrelationIdMiddleware.HeaderName] = overLength;

        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask, NullLogger<CorrelationIdMiddleware>.Instance);

        await middleware.InvokeAsync(httpContext);
        await responseFeature.FireOnStartingAsync();

        var storedValue = httpContext.Items[CorrelationIdMiddleware.ItemsKey] as string;
        storedValue.Should().NotBe(overLength);
        storedValue!.Length.Should().Be(32);
    }

    [Fact]
    public async Task InvokeAsync_RejectedValue_NeverReachesActivityBaggageOrResponseHeaderVerbatim()
    {
        // The regression this capability's acceptance criteria specifically calls for: a rejected
        // caller-supplied value must never reach Activity.Current.Baggage or the response header —
        // a fresh well-formed value is generated instead (T-55).
        using var activity = new Activity("test-activity").Start();
        var httpContext = CreateHttpContext(out var responseFeature);
        const string rejectedValue = "invalid value with spaces";
        httpContext.Request.Headers[CorrelationIdMiddleware.HeaderName] = rejectedValue;

        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask, NullLogger<CorrelationIdMiddleware>.Instance);

        await middleware.InvokeAsync(httpContext);
        await responseFeature.FireOnStartingAsync();

        Activity.Current!.Baggage.Should().NotContain(kv => kv.Key == CorrelationIdMiddleware.BaggageKey && kv.Value == rejectedValue);
        httpContext.Response.Headers[CorrelationIdMiddleware.HeaderName].ToString().Should().NotBe(rejectedValue);
        activity.Stop();
    }

    [Theory]
    [InlineData("550e8400-e29b-41d4-a716-446655440000")] // well-formed GUID (with dashes)
    [InlineData("550e8400e29b41d4a716446655440000")] // well-formed GUID ("N" format, no dashes)
    [InlineData("01ARZ3NDEKTSV4RRFFQ69G5FAV")] // well-formed ULID shape
    public async Task InvokeAsync_WellFormedCallerSuppliedValue_PreservedUnchangedEndToEnd(string wellFormedValue)
    {
        using var activity = new Activity("test-activity").Start();
        var httpContext = CreateHttpContext(out var responseFeature);
        httpContext.Request.Headers[CorrelationIdMiddleware.HeaderName] = wellFormedValue;

        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask, NullLogger<CorrelationIdMiddleware>.Instance);

        await middleware.InvokeAsync(httpContext);
        await responseFeature.FireOnStartingAsync();

        httpContext.Items[CorrelationIdMiddleware.ItemsKey].Should().Be(wellFormedValue);
        Activity.Current!.Baggage.Should().Contain(kv => kv.Key == CorrelationIdMiddleware.BaggageKey && kv.Value == wellFormedValue);
        httpContext.Response.Headers[CorrelationIdMiddleware.HeaderName].ToString().Should().Be(wellFormedValue);
        activity.Stop();
    }

    [Fact]
    public async Task InvokeAsync_MaxLengthConfiguredIndependently_AppliesConfiguredValue()
    {
        var httpContext = CreateHttpContext(out var responseFeature);
        var tenCharValue = new string('a', 10);
        httpContext.Request.Headers[CorrelationIdMiddleware.HeaderName] = tenCharValue;
        var options = new CorrelationIdOptions { MaxLength = 5 }; // Shorter than the 10-char value.

        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask, NullLogger<CorrelationIdMiddleware>.Instance, options);

        await middleware.InvokeAsync(httpContext);
        await responseFeature.FireOnStartingAsync();

        var storedValue = httpContext.Items[CorrelationIdMiddleware.ItemsKey] as string;
        storedValue.Should().NotBe(tenCharValue, "the configured MaxLength of 5 must reject a 10-character value");
    }

    [Fact]
    public async Task InvokeAsync_AllowedCharacterPatternConfiguredIndependently_AppliesConfiguredValue()
    {
        var httpContext = CreateHttpContext(out var responseFeature);
        const string valueWithDot = "abc.def";
        httpContext.Request.Headers[CorrelationIdMiddleware.HeaderName] = valueWithDot;
        // Restrict to lowercase letters only — the default pattern would otherwise allow '.'.
        var options = new CorrelationIdOptions { AllowedCharacterPattern = "^[a-z]+$" };

        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask, NullLogger<CorrelationIdMiddleware>.Instance, options);

        await middleware.InvokeAsync(httpContext);
        await responseFeature.FireOnStartingAsync();

        var storedValue = httpContext.Items[CorrelationIdMiddleware.ItemsKey] as string;
        storedValue.Should().NotBe(valueWithDot, "the configured stricter pattern must reject a value containing '.'");
    }

    [Fact]
    public void CorrelationIdOptions_Defaults_AreDocumentedConservativeValues()
    {
        var options = new CorrelationIdOptions();

        options.MaxLength.Should().Be(128);
        options.AllowedCharacterPattern.Should().Be("^[A-Za-z0-9\\-_:.]+$");
    }

    [Fact]
    public async Task InvokeAsync_NoOptionsSuppliedToConstructor_UsesDocumentedDefaultOptions()
    {
        // A host that calls UseSharedKernelCorrelationId() without ever calling
        // AddSharedKernelCorrelationId() must still apply the default-safe validation rather than
        // crashing on an unresolvable DI dependency.
        var httpContext = CreateHttpContext(out var responseFeature);
        var overLength = new string('a', 200);
        httpContext.Request.Headers[CorrelationIdMiddleware.HeaderName] = overLength;

        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask, NullLogger<CorrelationIdMiddleware>.Instance, options: null);

        await middleware.InvokeAsync(httpContext);
        await responseFeature.FireOnStartingAsync();

        var storedValue = httpContext.Items[CorrelationIdMiddleware.ItemsKey] as string;
        storedValue.Should().NotBe(overLength);
        storedValue!.Length.Should().Be(32);
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
