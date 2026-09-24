using System.Diagnostics;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OtelLogRecord = OpenTelemetry.Logs.LogRecord;
using SharedKernel.Presentation.WebApi.Correlation;
using SharedKernel.Presentation.WebApi.Errors;
using SharedKernel.Presentation.WebApi.Options;
using SharedKernel.Presentation.WebApi.Tests.TestSupport;
using SharedKernel.Primitives.Logging;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Primitives.Results;
using SharedKernel.Testing.Logging;
using Xunit;

namespace SharedKernel.Presentation.WebApi.Tests.Integration;

/// <summary>
/// Design D5/D16 with R3: a valid inbound correlation id is kept, an invalid one replaced (and never logged), a missing
/// one becomes the trace id, and the id reaches the accessor, baggage, log records, the response header and error
/// bodies — while baggage the caller sent reaches neither the activity nor any log record unless trusted.
/// </summary>
public sealed class CorrelationIdTests
{
    private const string TraceParentHeader = "traceparent";

    private const string TraceId = "0af7651916cd43dd8448eb211c80319c";

    private const string BaggageHeader = "baggage";

    // A key 13's log processor copies: a caller's item under it would reach every log record of the request as if the
    // platform had resolved it, which only the edge's removal prevents.
    private const string ForgedKey = WellKnownBaggageKeys.TenantId;

    [Theory]
    [InlineData("abc-123")]
    [InlineData("0f8fad5b-d9cb-469f-a165-70867728950e")]
    [InlineData("0f8fad5bd9cb469fa16570867728950e")]
    [InlineData("01ARZ3NDEKTSV4RRFFQ69G5FAV")]
    [InlineData("tenant:orders.v2_17")]
    public async Task ValidInboundId_IsKept_AndReachesTheAccessor(string inbound)
    {
        await using var app = await StartAsync();

        var (header, accessor) = await SendAsync(app, inbound);

        header.Should().Be(inbound);
        accessor.Should().Be(inbound);
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("<script>")]
    [InlineData("line\u0001break")]
    public async Task InvalidInboundId_IsReplaced_AndOnlyItsLengthIsLogged(string inbound)
    {
        var logs = new InMemoryLoggerFactory();
        await using var app = await StartAsync(loggerFactory: logs);

        var (header, accessor) = await SendAsync(app, inbound);

        header.Should().NotBe(inbound).And.MatchRegex("^[0-9a-f]{32}$");
        accessor.Should().Be(header);
        var record = logs.GetLogger(typeof(CorrelationIdMiddleware).FullName!).Records
            .Should().ContainSingle(r => r.EventId.Id == LoggingEventIdRanges.Presentation + 6).Subject;
        record.Message.Should().NotContain(inbound);
        record.TryGetProperty("CorrelationIdLength", out var length).Should().BeTrue();
        length.Should().Be(inbound.Length);
    }

    [Fact]
    public async Task OverlongInboundId_IsReplaced()
    {
        await using var app = await StartAsync();

        var (header, _) = await SendAsync(app, new string('a', 129));

        header.Should().HaveLength(32);
    }

    [Fact]
    public async Task MissingId_BecomesTheTraceId()
    {
        using var listener = ListenToAspNetCore();
        await using var app = await StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/id");
        request.Headers.Add(TraceParentHeader, $"00-{TraceId}-b7ad6b7169203331-01");

        using var response = await app.GetTestClient().SendAsync(request);

        response.Headers.GetValues(WellKnownHeaders.CorrelationId).Should().ContainSingle().Which.Should().Be(TraceId);
        (await response.Content.ReadAsStringAsync()).Should().Be(TraceId);
    }

    [Fact]
    public async Task ResolvedId_IsAddedToBaggage()
    {
        using var listener = ListenToAspNetCore();
        await using var app = await StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/baggage");
        request.Headers.Add(WellKnownHeaders.CorrelationId, "flow-9");

        using var response = await app.GetTestClient().SendAsync(request);

        (await response.Content.ReadAsStringAsync()).Should().Be("flow-9");
    }

    [Fact]
    public async Task ResolvedId_ReachesLogRecords_ThroughBaggage()
    {
        using var listener = ListenToAspNetCore();
        var captured = new List<IReadOnlyList<KeyValuePair<string, object?>>?>();
        await using var app = await StartAsync(configureBuilder: builder => builder.Logging.AddOpenTelemetry(options =>
        {
            options.AddProcessor(new BaggageToAttributesProcessor());
            options.AddProcessor(new CapturingProcessor(captured));
        }));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/log");
        request.Headers.Add(WellKnownHeaders.CorrelationId, "flow-10");

        using var response = await app.GetTestClient().SendAsync(request);

        response.EnsureSuccessStatusCode();
        captured.Should().Contain(attributes => attributes != null && attributes.Any(attribute =>
            attribute.Key == WellKnownBaggageKeys.CorrelationId && Equals(attribute.Value, "flow-10")));
    }

    [Fact]
    public async Task R3_ForgedInboundBaggage_NeverReachesTheActivity_OrALogRecord()
    {
        using var listener = ListenToAspNetCore();
        var captured = new List<IReadOnlyList<KeyValuePair<string, object?>>?>();
        await using var app = await StartAsync(configureBuilder: builder => CaptureLogAttributes(builder, captured));

        var (activityBaggage, response) = await SendWithForgedBaggageAsync(app);

        response.EnsureSuccessStatusCode();
        activityBaggage.Should().NotContain(ForgedKey).And.Contain(WellKnownBaggageKeys.CorrelationId);
        captured.Should().NotBeEmpty();
        captured.Should().NotContain(attributes => attributes != null && attributes.Any(attribute => attribute.Key == ForgedKey));
        captured.Should().Contain(attributes => attributes != null && attributes.Any(attribute =>
            attribute.Key == WellKnownBaggageKeys.CorrelationId && Equals(attribute.Value, "flow-13")));
    }

    [Fact]
    public async Task R3_TrustInboundBaggage_KeepsTheCallersBaggage()
    {
        // The opt-in for services behind a sanitizing gateway — and the proof that the default test above is not vacuous.
        using var listener = ListenToAspNetCore();
        var captured = new List<IReadOnlyList<KeyValuePair<string, object?>>?>();
        await using var app = await StartAsync(
            options => options.TrustInboundBaggage = true,
            configureBuilder: builder => CaptureLogAttributes(builder, captured));

        var (activityBaggage, response) = await SendWithForgedBaggageAsync(app);

        response.EnsureSuccessStatusCode();
        activityBaggage.Should().Contain(ForgedKey);
        captured.Should().Contain(attributes => attributes != null && attributes.Any(attribute => attribute.Key == ForgedKey));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void R3_HostingsPropagator_ReadsNoInboundBaggage_UnlessTrusted(bool trust, bool readsBaggage)
    {
        var options = new SharedKernelWebApiOptions { TrustInboundBaggage = trust };
        var propagator = new InboundBaggagePropagator(DistributedContextPropagator.CreateDefaultPropagator(), Microsoft.Extensions.Options.Options.Create(options));
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
    public void R3_RemoveAll_RemovesEveryItem_DuplicatesIncluded()
    {
        using var activity = new Activity("request");
        activity.AddBaggage("tenant.id", "a");
        activity.AddBaggage("tenant.id", "b");
        activity.AddBaggage("user.id", "c");

        InboundBaggage.RemoveAll(activity);

        activity.Baggage.Should().BeEmpty();
    }

    [Fact]
    public async Task ErrorResponse_CarriesTheId_InHeaderAndBody()
    {
        await using var app = await StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/failure");
        request.Headers.Add(WellKnownHeaders.CorrelationId, "flow-11");

        using var response = await app.GetTestClient().SendAsync(request);

        var problem = await response.ShouldBeProblemAsync(StatusCodes.Status404NotFound, "order.not_found");
        problem.GetProperty(ProblemDetailsExtensionNames.CorrelationId).GetString().Should().Be("flow-11");
    }

    [Fact]
    public async Task CustomPattern_IsApplied()
    {
        await using var app = await StartAsync(options => options.CorrelationId.AllowedCharacterPattern = "^[0-9]+$");

        var (digits, _) = await SendAsync(app, "12345");
        var (letters, _) = await SendAsync(app, "abc");

        digits.Should().Be("12345");
        letters.Should().NotBe("abc");
    }

    [Fact]
    public async Task Disabled_WritesNoHeader_AndTheAccessorIsNull()
    {
        await using var app = await StartAsync(options => options.CorrelationId.Enabled = false);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/id");
        request.Headers.Add(WellKnownHeaders.CorrelationId, "flow-12");

        using var response = await app.GetTestClient().SendAsync(request);

        response.Headers.Contains(WellKnownHeaders.CorrelationId).Should().BeFalse();
        (await response.Content.ReadAsStringAsync()).Should().Be("(none)");
    }

    [Fact]
    public void Accessor_OutsideThePipeline_IsNull()
    {
        new DefaultHttpContext().GetCorrelationId().Should().BeNull();
    }

    private static Task<WebApplication> StartAsync(
        Action<SharedKernelWebApiOptions>? configure = null,
        Action<WebApplicationBuilder>? configureBuilder = null,
        InMemoryLoggerFactory? loggerFactory = null) =>
        WebApiTestHost.StartAsync(
            app =>
            {
                app.MapGet("/id", (HttpContext context) => context.GetCorrelationId() ?? "(none)");
                app.MapGet("/baggage", () => Activity.Current?.GetBaggageItem(WellKnownBaggageKeys.CorrelationId) ?? "(none)");
                app.MapGet("/log", (ILoggerFactory factory) =>
                {
                    factory.CreateLogger("CorrelationIdTests").LogInformation("Handler ran.");
                    return "ok";
                });
                app.MapGet("/failure", () => Result<string>.Failure(TestErrors.OrderNotFound).ToOk());
                app.MapGet("/log-baggage", (ILoggerFactory factory) =>
                {
                    factory.CreateLogger("CorrelationIdTests").LogInformation("Handler ran.");
                    return string.Join(",", Activity.Current?.Baggage.Select(item => item.Key) ?? []);
                });
            },
            configureBuilder,
            configure,
            loggerFactory: loggerFactory);

    private static void CaptureLogAttributes(WebApplicationBuilder builder, List<IReadOnlyList<KeyValuePair<string, object?>>?> captured) =>
        builder.Logging.AddOpenTelemetry(options =>
        {
            options.AddProcessor(new BaggageToAttributesProcessor());
            options.AddProcessor(new CapturingProcessor(captured));
        });

    private static async Task<(string[] ActivityBaggage, HttpResponseMessage Response)> SendWithForgedBaggageAsync(WebApplication app)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/log-baggage");
        request.Headers.Add(WellKnownHeaders.CorrelationId, "flow-13");
        request.Headers.Add(BaggageHeader, $"{ForgedKey}=forged-tenant");

        var response = await app.GetTestClient().SendAsync(request);
        var keys = await response.Content.ReadAsStringAsync();
        return (keys.Split(',', StringSplitOptions.RemoveEmptyEntries), response);
    }

    private static async Task<(string Header, string Accessor)> SendAsync(WebApplication app, string inbound)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/id");
        request.Headers.TryAddWithoutValidation(WellKnownHeaders.CorrelationId, inbound);

        using var response = await app.GetTestClient().SendAsync(request);

        var header = response.Headers.GetValues(WellKnownHeaders.CorrelationId).Single();
        return (header, await response.Content.ReadAsStringAsync());
    }

    private static ActivityListener ListenToAspNetCore()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Microsoft.AspNetCore",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    /// <summary>
    /// Mirrors 13.ServiceDefaults' <c>BaggageLogRecordProcessor</c> (P-562 X2) without referencing that package: copies
    /// the two baggage items platform middleware writes, <c>correlation.id</c> and <c>TenantId</c>, onto log records —
    /// never over an attribute already there, and never a value with a control or line-break character. Every other
    /// baggage item is ignored.
    /// </summary>
    private sealed class BaggageToAttributesProcessor : BaseProcessor<OtelLogRecord>
    {
        private const char LineSeparator = (char)0x2028;

        private const char ParagraphSeparator = (char)0x2029;

        private static readonly string[] PlatformKeys = [WellKnownBaggageKeys.CorrelationId, WellKnownBaggageKeys.TenantId];

        public override void OnEnd(OtelLogRecord data)
        {
            if (Activity.Current is not { } activity)
            {
                return;
            }

            var attributes = new List<KeyValuePair<string, object?>>(data.Attributes ?? []);
            foreach (var key in PlatformKeys)
            {
                if (activity.GetBaggageItem(key) is { } value && IsLoggable(value) && attributes.All(attribute => attribute.Key != key))
                {
                    attributes.Add(new KeyValuePair<string, object?>(key, value));
                }
            }

            data.Attributes = attributes;
        }

        // A log viewer renders these as line breaks: C0, DEL and C1 controls, and the Unicode line and paragraph separators.
        private static bool IsLoggable(string value) =>
            !value.Any(character => char.IsControl(character) || character is LineSeparator or ParagraphSeparator);
    }

    private sealed class CapturingProcessor(List<IReadOnlyList<KeyValuePair<string, object?>>?> sink) : BaseProcessor<OtelLogRecord>
    {
        public override void OnEnd(OtelLogRecord data)
        {
            lock (sink)
            {
                sink.Add(data.Attributes is null ? null : [.. data.Attributes]);
            }
        }
    }
}
