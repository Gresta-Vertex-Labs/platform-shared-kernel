using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using SharedKernel.Presentation.WebApi;
using SharedKernel.Security.Abstractions;
using SharedKernel.ServiceDefaults.Security;
using SharedKernel.Primitives.Propagation;
using SharedKernel.ServiceDefaults.Telemetry;

namespace SharedKernel.ServiceDefaults.Tests.Telemetry;

/// <summary>
/// P-562 X2 through the real wiring (<c>AddSharedKernelTelemetry</c>, with and without <c>14.Presentation</c>'s
/// WebApi core): the W3C <c>baggage</c> header an anonymous caller sends reaches neither OpenTelemetry's baggage
/// store, a log record nor an outgoing call, while baggage the service sets itself still leaves with its outgoing
/// calls, as does the trace context.
/// </summary>
/// <remarks>
/// The outgoing call is real: an <see cref="HttpClient"/> over <see cref="SocketsHttpHandler"/>, so .NET's
/// <c>DiagnosticsHandler</c> and OpenTelemetry's HttpClient instrumentation both run and write their headers. Only
/// the connection is refused, and the headers the request left with are read afterwards.
/// </remarks>
public sealed class InboundBaggageTests
{
    [Fact]
    public async Task CallersBaggage_NeverReachesOpenTelemetryBaggage_WhileTheServicesOwnStillLeaves()
    {
        await using var app = await ProbeHost.StartAsync(withWebApi: false);

        var probe = await ProbeHost.SendAsync(app, setServiceBaggage: true);

        probe.OpenTelemetryBaggage.Should().BeEmpty("the caller's baggage header is not read into Baggage.Current");
        ProbeHost.ParseBaggage(probe.OutgoingBaggage).Should().Equal(
            new Dictionary<string, string> { [ProbeHost.ServiceBaggageKey] = ProbeHost.ServiceBaggageValue });
        probe.OutgoingTraceParent.Should().Contain(ProbeHost.TraceId, "trace context is still read from the request");
    }

    [Fact]
    public async Task WithTheWebApiCore_CallersBaggage_ReachesNoStore_NoLogRecord_AndNoOutgoingCall()
    {
        var logs = new ConcurrentQueue<KeyValuePair<string, object?>[]>();
        await using var app = await ProbeHost.StartAsync(withWebApi: true, logs);

        var probe = await ProbeHost.SendAsync(app, setServiceBaggage: false, correlationId: "flow-x2");

        probe.OpenTelemetryBaggage.Should().BeEmpty();
        probe.ActivityBaggage.Should().Equal($"{WellKnownBaggageKeys.CorrelationId}=flow-x2");
        ProbeHost.ParseBaggage(probe.OutgoingBaggage).Should().Equal(
            new Dictionary<string, string> { [WellKnownBaggageKeys.CorrelationId] = "flow-x2" },
            "the platform's own Activity baggage is what leaves");

        logs.Should().NotBeEmpty();
        logs.SelectMany(attributes => attributes).Should().NotContain(attribute =>
            attribute.Key == "SubjectId" || ProbeHost.ForgedValues.Contains(attribute.Value as string));
        logs.Should().Contain(attributes => attributes.Any(attribute =>
            attribute.Key == WellKnownBaggageKeys.CorrelationId && Equals(attribute.Value, "flow-x2")));
    }
}

/// <summary>A service that reports what it sees of the caller's baggage and makes one outgoing call.</summary>
internal static class ProbeHost
{
    public const string TraceId = "0af7651916cd43dd8448eb211c80319c";
    public const string ServiceBaggageKey = "flow";
    public const string ServiceBaggageValue = "checkout";

    private const string TraceParentHeader = "traceparent";
    private const string BaggageHeader = "baggage";
    private const string OtlpTimeoutKey = "OTEL_EXPORTER_OTLP_TIMEOUT";
    private const string ForgedBaggage = "TenantId=victim-tenant,SubjectId=admin,correlation.id=forged-flow";

    public static readonly string[] ForgedValues = ["victim-tenant", "admin", "forged-flow"];

    public static async Task<WebApplication> StartAsync(
        bool withWebApi,
        ConcurrentQueue<KeyValuePair<string, object?>[]>? logs = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();

        // No collector runs here; bound the OTLP exporters' flush on shutdown instead of waiting on refused connections.
        builder.Configuration[OtlpTimeoutKey] = "100";
        builder.AddSharedKernelTelemetry("x2-probe");

        if (withWebApi)
        {
            // P-579: the edge that refuses the caller's Activity baggage is the request context, run before the WebApi.
            builder.Services.AddSingleton<IUserContext>(AnonymousUserContext.Instance);
            builder.Services.AddSharedKernelRequestContext();
            builder.AddSharedKernelWebApi();
        }

        if (logs is not null)
        {
            // Registered after AddSharedKernelTelemetry, so it runs after BaggageLogRecordProcessor.
            builder.Services.ConfigureOpenTelemetryLoggerProvider(logging =>
                logging.AddProcessor(new CapturingProcessor(logs)));
        }

        var app = builder.Build();

        if (withWebApi)
        {
            app.UseSharedKernelRequestContext();
            app.UseSharedKernelWebApi();
        }

        app.MapGet("/probe", async (bool serviceBaggage, ILoggerFactory loggers) =>
        {
            loggers.CreateLogger(nameof(InboundBaggageTests)).LogInformation("Probe ran.");

            var openTelemetryBaggage = Baggage.Current.GetBaggage().Select(item => $"{item.Key}={item.Value}").ToArray();
            var activityBaggage = Activity.Current?.Baggage.Select(item => $"{item.Key}={item.Value}").ToArray() ?? [];

            if (serviceBaggage)
            {
                Baggage.SetBaggage(ServiceBaggageKey, ServiceBaggageValue);
            }

            using var handler = new OfflineCapturingHandler();
            using var client = new HttpClient(handler);
            using var outgoing = await client.GetAsync(new Uri("http://downstream.test/orders"));

            return new ProbeResult(openTelemetryBaggage, activityBaggage, handler.Baggage, handler.TraceParent);
        });

        await app.StartAsync();
        return app;
    }

    public static async Task<ProbeResult> SendAsync(WebApplication app, bool setServiceBaggage, string? correlationId = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/probe?serviceBaggage={setServiceBaggage}");
        request.Headers.Add(TraceParentHeader, $"00-{TraceId}-b7ad6b7169203331-01");
        request.Headers.Add(BaggageHeader, ForgedBaggage);
        if (correlationId is not null)
        {
            request.Headers.Add(WellKnownHeaders.CorrelationId, correlationId);
        }

        using var response = await app.GetTestClient().SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ProbeResult>())!;
    }

    /// <summary>
    /// Reads a W3C <c>baggage</c> header into key/value pairs. The two writers space it differently: OpenTelemetry
    /// writes <c>key=value</c>, .NET's propagator <c>key = value</c>.
    /// </summary>
    public static Dictionary<string, string> ParseBaggage(string? header) =>
        (header ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(member => member.Split('=', 2, StringSplitOptions.TrimEntries))
            .ToDictionary(pair => Uri.UnescapeDataString(pair[0]), pair => Uri.UnescapeDataString(pair[1]));

    private sealed class CapturingProcessor(ConcurrentQueue<KeyValuePair<string, object?>[]> sink) : BaseProcessor<LogRecord>
    {
        public override void OnEnd(LogRecord data) => sink.Enqueue(data.Attributes is null ? [] : [.. data.Attributes]);
    }

    /// <summary>
    /// Sends through a real <see cref="SocketsHttpHandler"/> whose connection is refused, then records the propagation
    /// headers the request carried: every handler that writes them has run by then.
    /// </summary>
    private sealed class OfflineCapturingHandler() : DelegatingHandler(new SocketsHttpHandler
    {
        UseProxy = false,
        ConnectCallback = static (_, _) => ValueTask.FromException<Stream>(new IOException("No network in this test.")),
    })
    {
        public string? Baggage { get; private set; }

        public string? TraceParent { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            try
            {
                await base.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException)
            {
                // Expected: the connection is refused.
            }

            Baggage = Read(request, BaggageHeader);
            TraceParent = Read(request, TraceParentHeader);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }

        private static string? Read(HttpRequestMessage request, string name) =>
            request.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : null;
    }
}

/// <summary>What the probe endpoint saw and sent.</summary>
internal sealed record ProbeResult(
    string[] OpenTelemetryBaggage,
    string[] ActivityBaggage,
    string? OutgoingBaggage,
    string? OutgoingTraceParent);
