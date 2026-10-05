using System.Diagnostics;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SharedKernel.Execution.Context;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Security.Abstractions;
using SharedKernel.Testing.Security;

namespace SharedKernel.ServiceDefaults.Security.Tests;

/// <summary>
/// P-579 (ported from <c>SharedKernel.Presentation.WebApi</c>'s R3 tests): the edge that owns the correlation id also
/// refuses the caller's W3C baggage. Hosting's propagator takes none from the request, the middleware removes any item
/// that reached the request's activity before it adds the correlation id, and <see cref="RequestContextOptions.TrustInboundBaggage"/>
/// keeps it for a service behind a sanitizing gateway.
/// </summary>
public sealed class InboundBaggageTests
{
    private const string TraceParentHeader = "traceparent";

    private const string BaggageHeader = "baggage";

    private const string TraceId = "0af7651916cd43dd8448eb211c80319c";

    // A key the platform's log processor copies: a caller's item under it would reach every log record of the request.
    private const string ForgedKey = WellKnownBaggageKeys.TenantId;

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void HostingsPropagator_ReadsNoInboundBaggage_UnlessTrusted(bool trust, bool readsBaggage)
    {
        var options = Options.Create(new RequestContextOptions { TrustInboundBaggage = trust });
        var propagator = new InboundBaggagePropagator(DistributedContextPropagator.CreateDefaultPropagator(), options);
        var headers = new Dictionary<string, string>
        {
            [TraceParentHeader] = $"00-{TraceId}-b7ad6b7169203331-01",
            [BaggageHeader] = $"{ForgedKey}=forged-tenant",
        };

        void Getter(object? carrier, string name, out string? value, out IEnumerable<string>? values)
        {
            values = null;
            value = ((Dictionary<string, string>)carrier!).GetValueOrDefault(name);
        }

        propagator.ExtractTraceIdAndState(headers, Getter, out var traceParent, out _);
        var baggage = propagator.ExtractBaggage(headers, Getter);

        traceParent.Should().Contain(TraceId, "trace context is always read");
        (baggage?.Any(item => item.Key == ForgedKey) == true).Should().Be(readsBaggage);
    }

    [Fact]
    public void RemoveAll_RemovesEveryItem_DuplicatesIncluded()
    {
        using var activity = new Activity("request");
        activity.AddBaggage("tenant.id", "a");
        activity.AddBaggage("tenant.id", "b");
        activity.AddBaggage("user.id", "c");

        InboundBaggage.RemoveAll(activity);

        activity.Baggage.Should().BeEmpty();
    }

    [Fact]
    public async Task Middleware_RemovesTheCallersBaggage_AndKeepsOnlyTheCorrelationId()
    {
        using var activity = new Activity("request").Start();
        activity.AddBaggage(ForgedKey, "forged-tenant");
        var http = CreateHttpContext();
        http.Request.Headers[WellKnownHeaders.CorrelationId] = "flow-13";
        string[] seen = [];

        await new RequestContextMiddleware(_ => { seen = [.. activity.Baggage.Select(item => item.Key)]; return Task.CompletedTask; }, NullLogger<RequestContextMiddleware>.Instance)
            .InvokeAsync(http);

        seen.Should().Equal(WellKnownBaggageKeys.CorrelationId);
        activity.GetBaggageItem(WellKnownBaggageKeys.CorrelationId).Should().Be("flow-13");
    }

    [Fact]
    public async Task Middleware_KeepsTheCallersBaggage_WhenTrusted()
    {
        // The opt-in for services behind a sanitizing gateway, and the proof that the test above is not vacuous.
        using var activity = new Activity("request").Start();
        activity.AddBaggage(ForgedKey, "forged-tenant");
        var http = CreateHttpContext();
        string? kept = null;

        await new RequestContextMiddleware(
                _ => { kept = activity.GetBaggageItem(ForgedKey); return Task.CompletedTask; },
                NullLogger<RequestContextMiddleware>.Instance,
                Options.Create(new RequestContextOptions { TrustInboundBaggage = true }))
            .InvokeAsync(http);

        kept.Should().Be("forged-tenant");
    }

    [Fact]
    public void AddSharedKernelRequestContext_DecoratesAnAlreadyRegisteredPropagator()
    {
        var original = DistributedContextPropagator.CreatePassThroughPropagator();
        var services = new ServiceCollection();
        services.AddSingleton(original);

        services.AddSharedKernelRequestContext();
        using var provider = services.BuildServiceProvider();

        var propagator = provider.GetRequiredService<DistributedContextPropagator>();
        propagator.Should().BeOfType<InboundBaggagePropagator>().Which.Inner.Should().BeSameAs(original);
    }

    [Fact]
    public void AddSharedKernelRequestContext_RegistersThePropagator_WhenNoneIsRegisteredYet()
    {
        var services = new ServiceCollection();

        services.AddSharedKernelRequestContext();
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<DistributedContextPropagator>().Should().BeOfType<InboundBaggagePropagator>();
    }

    [Fact]
    public void AddSharedKernelRequestContext_CalledTwice_DecoratesOnce_AndAppliesEveryCallback()
    {
        var services = new ServiceCollection();

        services.AddSharedKernelRequestContext();
        services.AddSharedKernelRequestContext(options => options.TrustInboundBaggage = true);
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<DistributedContextPropagator>()
            .Should().BeOfType<InboundBaggagePropagator>().Which.Inner.Should().NotBeOfType<InboundBaggagePropagator>();
        provider.GetRequiredService<IOptions<RequestContextOptions>>().Value.TrustInboundBaggage.Should().BeTrue();
    }

    [Fact]
    public async Task Context_KeptAfterTheRequest_StillAnswersFromTheCallerTheRequestAuthenticated()
    {
        // A SignalR connection keeps the context of the request that opened it; with long polling that request, and
        // its services, are gone before the connection's hub methods run.
        var services = new ServiceCollection();
        services.AddScoped<IUserContext>(_ => new FakeUserContext { SubjectId = "user-7" });
        services.AddSharedKernelRequestContext();
        using var provider = services.BuildServiceProvider();
        IRequestContext? kept = null;

        using (var scope = provider.CreateScope())
        {
            var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
            http.Features.Set<IHttpResponseFeature>(new HttpResponseFeature());
            await new RequestContextMiddleware(_ => { kept = RequestContextScope.Current; return Task.CompletedTask; }, NullLogger<RequestContextMiddleware>.Instance)
                .InvokeAsync(http);
        }

        kept!.UserId.Should().Be("user-7");
        (await kept.HasPermissionAsync("orders.read", CancellationToken.None)).Should().BeFalse();
    }

    private static DefaultHttpContext CreateHttpContext()
    {
        var services = new ServiceCollection();
        services.AddScoped<IUserContext>(_ => new FakeUserContext());
        services.AddSharedKernelRequestContext();

        var http = new DefaultHttpContext { RequestServices = services.BuildServiceProvider().CreateScope().ServiceProvider };
        http.Features.Set<IHttpResponseFeature>(new HttpResponseFeature());
        return http;
    }
}
