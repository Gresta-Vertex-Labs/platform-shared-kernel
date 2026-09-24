using System.Diagnostics;
using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using SharedKernel.ServiceDefaults.Telemetry;

namespace SharedKernel.ServiceDefaults.Tests.Telemetry;

/// <summary>
/// Serializes the tests that replace OpenTelemetry's process-wide default propagator, so no host test runs while
/// another propagator is installed.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DefaultPropagatorCollection
{
    /// <summary>The collection name.</summary>
    public const string Name = "OpenTelemetry default propagator";
}

/// <summary>P-562 X2: OpenTelemetry's baggage store is never filled from an incoming request.</summary>
[Collection(DefaultPropagatorCollection.Name)]
public sealed class RequestBaggageRefusingPropagatorTests
{
    private const string TraceId = "0af7651916cd43dd8448eb211c80319c";
    private const string TraceParent = $"00-{TraceId}-b7ad6b7169203331-01";
    private const string TraceParentHeader = "traceparent";
    private const string BaggageHeader = "baggage";

    [Fact]
    public void Extract_FromAnIncomingRequest_ReadsTheTraceContext_AndNoBaggage()
    {
        var request = RequestWithForgedBaggage();

        var context = new RequestBaggageRefusingPropagator(SdkDefaultPropagator()).Extract(default, request, HeaderValues);

        context.ActivityContext.TraceId.ToHexString().Should().Be(TraceId);
        context.Baggage.GetBaggage().Should().BeEmpty();
    }

    [Fact]
    public void Extract_FromAnIncomingRequest_WithoutTheDecorator_ReadsTheCallersBaggage()
    {
        // The SDK's own behaviour, which the test above would otherwise prove nothing against.
        var context = SdkDefaultPropagator().Extract(default, RequestWithForgedBaggage(), HeaderValues);

        context.Baggage.GetBaggage().Should().ContainKey("TenantId");
    }

    [Fact]
    public void Extract_FromAnotherCarrier_KeepsItsBaggage()
    {
        // Temporal's tracing interceptor extracts workflow headers written by the platform's own client.
        var headers = new Dictionary<string, string>
        {
            [TraceParentHeader] = TraceParent,
            [BaggageHeader] = "flow=checkout",
        };

        var context = new RequestBaggageRefusingPropagator(SdkDefaultPropagator()).Extract(default, headers, DictionaryValues);

        context.ActivityContext.TraceId.ToHexString().Should().Be(TraceId);
        context.Baggage.GetBaggage("flow").Should().Be("checkout");
    }

    [Fact]
    public void Inject_WritesTheServicesBaggage_AndTraceContext()
    {
        var headers = new Dictionary<string, string>();
        var activityContext = new ActivityContext(
            ActivityTraceId.CreateFromString(TraceId),
            ActivitySpanId.CreateRandom(),
            ActivityTraceFlags.Recorded);
        var baggage = Baggage.Create(new Dictionary<string, string> { ["flow"] = "checkout" });

        new RequestBaggageRefusingPropagator(SdkDefaultPropagator())
            .Inject(new PropagationContext(activityContext, baggage), headers, static (carrier, name, value) => carrier[name] = value);

        headers[BaggageHeader].Should().Be("flow=checkout");
        headers[TraceParentHeader].Should().StartWith($"00-{TraceId}-");
    }

    [Fact]
    public void Fields_AreTheDecoratedPropagators()
    {
        var inner = SdkDefaultPropagator();

        new RequestBaggageRefusingPropagator(inner).Fields.Should().BeEquivalentTo(inner.Fields);
    }

    [Fact]
    public void Install_DecoratesTheDefaultPropagatorInPlace_Once()
    {
        var original = CurrentDefault();
        var serviceChoice = SdkDefaultPropagator();
        try
        {
            Sdk.SetDefaultTextMapPropagator(serviceChoice);

            RequestBaggageRefusingPropagator.Install();
            RequestBaggageRefusingPropagator.Install();

            var installed = Propagators.DefaultTextMapPropagator.Should().BeOfType<RequestBaggageRefusingPropagator>().Subject;
            installed.Inner.Should().BeSameAs(serviceChoice, "a propagator the service chose is decorated, not replaced");
        }
        finally
        {
            Sdk.SetDefaultTextMapPropagator(original);
        }
    }

    [Fact]
    public void Install_LeavesAPlainTraceContextPropagatorAlone()
    {
        // It reads no baggage, and the instrumentations skip their own extraction for it.
        var original = CurrentDefault();
        var traceContextOnly = new TraceContextPropagator();
        try
        {
            Sdk.SetDefaultTextMapPropagator(traceContextOnly);

            RequestBaggageRefusingPropagator.Install();

            Propagators.DefaultTextMapPropagator.Should().BeSameAs(traceContextOnly);
        }
        finally
        {
            Sdk.SetDefaultTextMapPropagator(original);
        }
    }

    [Fact]
    public async Task WithoutTheDecorator_TheInstrumentationFillsOpenTelemetryBaggage_AndForwardsIt()
    {
        // The path X2 closes, shown open, so InboundBaggageTests cannot pass vacuously: the ASP.NET Core
        // instrumentation reads the caller's header into Baggage.Current, and the HttpClient instrumentation sends it on.
        await using var app = await ProbeHost.StartAsync(withWebApi: false);
        var decorated = Propagators.DefaultTextMapPropagator.Should().BeOfType<RequestBaggageRefusingPropagator>().Subject;
        try
        {
            Sdk.SetDefaultTextMapPropagator(decorated.Inner);

            var probe = await ProbeHost.SendAsync(app, setServiceBaggage: false);

            probe.OpenTelemetryBaggage.Should().Contain("TenantId=victim-tenant");
            probe.OutgoingBaggage.Should().Contain("victim-tenant");
        }
        finally
        {
            Sdk.SetDefaultTextMapPropagator(decorated);
        }
    }

    [Fact]
    public async Task StartingAHost_InstallsTheDecorator()
    {
        var original = CurrentDefault();
        try
        {
            Sdk.SetDefaultTextMapPropagator(SdkDefaultPropagator());

            await using var app = await ProbeHost.StartAsync(withWebApi: false);

            Propagators.DefaultTextMapPropagator.Should().BeOfType<RequestBaggageRefusingPropagator>();
        }
        finally
        {
            Sdk.SetDefaultTextMapPropagator(original);
        }
    }

    // What the OpenTelemetry SDK installs by default: W3C trace context and W3C baggage.
    private static CompositeTextMapPropagator SdkDefaultPropagator() =>
        new([new TraceContextPropagator(), new BaggagePropagator()]);

    private static TextMapPropagator CurrentDefault()
    {
        // Sdk's static constructor installs the SDK default; run it first so the value restored is a real one.
        RuntimeHelpers.RunClassConstructor(typeof(Sdk).TypeHandle);
        return Propagators.DefaultTextMapPropagator;
    }

    private static HttpRequest RequestWithForgedBaggage()
    {
        var request = new DefaultHttpContext().Request;
        request.Headers[TraceParentHeader] = TraceParent;
        request.Headers[BaggageHeader] = "TenantId=victim-tenant,SubjectId=admin";
        return request;
    }

    // The getter shape OpenTelemetry's ASP.NET Core instrumentation passes.
    private static IEnumerable<string>? HeaderValues(HttpRequest request, string name) => request.Headers[name];

    private static IEnumerable<string>? DictionaryValues(Dictionary<string, string> carrier, string name) =>
        carrier.TryGetValue(name, out var value) ? [value] : null;
}
