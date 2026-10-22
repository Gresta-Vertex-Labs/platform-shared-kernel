using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using SharedKernel.Primitives.Logging;
using SharedKernel.Primitives.Propagation;
using SharedKernel.ServiceDefaults.Diagnostics;
using SharedKernel.ServiceDefaults.Extensions;
using SharedKernel.ServiceDefaults.Telemetry;
using SharedKernel.Testing.Logging;
using TestLogRecord = SharedKernel.Testing.Logging.LogRecord;

namespace SharedKernel.ServiceDefaults.Tests.Telemetry;

/// <summary>
/// The OTLP exporter mode: whether <c>AddSharedKernelTelemetry()</c> registers the exporter, proven against the real
/// exporter sending to a loopback collector, and the one start-up warning (13002). The decision is read from the host
/// configuration when the extension runs, so every test sets its configuration before composing.
/// </summary>
[Collection(NonParallelTelemetryCollection.Name)]
public sealed class OtlpExporterModeTests
{
    private const string Traces = "/v1/traces";
    private const string Metrics = "/v1/metrics";
    private const string Logs = "/v1/logs";

    private static readonly string[] AllSignals = [Traces, Metrics, Logs];

    /// <summary>
    /// A host builder with in-memory configuration: no endpoint anywhere (a blank value wins over the machine's
    /// environment), HTTP/protobuf, and a generous exporter timeout.
    /// </summary>
    private static HostApplicationBuilder NewBuilder(params (string Key, string? Value)[] settings)
    {
        var builder = Host.CreateApplicationBuilder();
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [OtlpEndpointDetector.EndpointKey] = string.Empty,
            ["OTEL_EXPORTER_OTLP_TRACES_ENDPOINT"] = string.Empty,
            ["OTEL_EXPORTER_OTLP_METRICS_ENDPOINT"] = string.Empty,
            ["OTEL_EXPORTER_OTLP_LOGS_ENDPOINT"] = string.Empty,
            ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf",
            ["OTEL_EXPORTER_OTLP_TIMEOUT"] = "5000",
        };
        foreach (var (key, value) in settings)
        {
            values[key] = value;
        }

        builder.Configuration.AddInMemoryCollection(values);
        return builder;
    }

    private static HashSet<string> ExportAllSignals(IHost host, OtlpCollector collector, string? correlationId = null)
    {
        var services = host.Services;
        var tracer = services.GetRequiredService<TracerProvider>();
        var meter = services.GetRequiredService<MeterProvider>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("otlp-mode-test");

        using var source = new ActivitySource("SharedKernel.OtlpModeTest");
        using var instruments = new Meter("SharedKernel.OtlpModeTest");
        var counter = instruments.CreateCounter<long>("sharedkernel.otlpmodetest.count", "{call}");

        using (var activity = source.StartActivity("work"))
        {
            if (correlationId is not null)
            {
                activity!.SetBaggage(WellKnownBaggageKeys.CorrelationId, correlationId);
            }

            counter.Add(1);
            logger.LogInformation("an export probe");
        }

        tracer.ForceFlush(10_000);
        meter.ForceFlush(10_000);
        services.GetService<LoggerProvider>()?.ForceFlush(10_000);
        return [.. collector.Paths];
    }

    private static OtlpExporterPlan SinglePlan(IHost host) => host.Services.GetServices<OtlpExporterPlan>().Single();

    private static Uri ExporterEndpoint(IHost host) =>
        host.Services.GetRequiredService<IOptionsFactory<OtlpExporterOptions>>().Create(Options.DefaultName).Endpoint;

    [Fact]
    public async Task WhenEndpointConfigured_WithNoEndpoint_RegistersNoExporterAndExportsNothing()
    {
        await using var collector = await OtlpCollector.StartAsync();
        var builder = NewBuilder();
        builder.AddSharedKernelTelemetry("svc", o => o.OtlpExporter = OtlpExporterMode.WhenEndpointConfigured);
        using var host = builder.Build();
        await host.StartAsync();

        SinglePlan(host).Registered.Should().BeFalse();
        ExportAllSignals(host, collector).Should().BeEmpty();
        await host.StopAsync();
    }

    [Fact]
    public async Task WhenEndpointConfigured_WithTheEndpoint_ExportsEverySignal()
    {
        await using var collector = await OtlpCollector.StartAsync();
        var builder = NewBuilder((OtlpEndpointDetector.EndpointKey, collector.Url));
        builder.AddSharedKernelTelemetry("svc", o => o.OtlpExporter = OtlpExporterMode.WhenEndpointConfigured);
        using var host = builder.Build();
        await host.StartAsync();

        SinglePlan(host).Registered.Should().BeTrue();
        ExportAllSignals(host, collector).Should().BeEquivalentTo(AllSignals);
        await host.StopAsync();
    }

    [Fact]
    public async Task ASignalsOwnEndpoint_IsNotHonouredByThePerSignalRegistration_SoItDoesNotCountAsConfigured()
    {
        // OpenTelemetry.Exporter.OpenTelemetryProtocol 1.16.0 reads OTEL_EXPORTER_OTLP_{TRACES|METRICS|LOGS}_ENDPOINT only
        // through UseOtlpExporter(); the AddOtlpExporter() this package keeps ignores them and sends to the local default.
        // If an upgrade changes that, this test fails and the detector must learn the per-signal variables.
        await using var collector = await OtlpCollector.StartAsync();
        var builder = NewBuilder(
            ("OTEL_EXPORTER_OTLP_TRACES_ENDPOINT", collector.Url + Traces),
            ("OTEL_EXPORTER_OTLP_METRICS_ENDPOINT", collector.Url + Metrics),
            ("OTEL_EXPORTER_OTLP_LOGS_ENDPOINT", collector.Url + Logs));
        builder.AddSharedKernelTelemetry("svc");
        using var host = builder.Build();
        await host.StartAsync();

        OtlpEndpointDetector.IsConfigured(host.Services.GetRequiredService<IConfiguration>()).Should().BeFalse();
        ExporterEndpoint(host).Should().Be(new Uri("http://localhost:4318"));
        ExportAllSignals(host, collector).Should().BeEmpty();
        await host.StopAsync();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a uri")]
    public void ABlankOrInvalidEndpoint_CountsAsNotConfigured_ForTheDetectorAndTheExporter(string value)
    {
        // The exporter ignores a blank or non-URI endpoint and falls back to its local default; the detector must agree,
        // or WhenEndpointConfigured would register an exporter that sends to localhost.
        var builder = NewBuilder((OtlpEndpointDetector.EndpointKey, value));
        builder.AddSharedKernelTelemetry("svc");
        using var host = builder.Build();

        var configuration = host.Services.GetRequiredService<IConfiguration>();
        OtlpEndpointDetector.IsConfigured(configuration).Should().BeFalse();
        ExporterEndpoint(host).Should().Be(new Uri("http://localhost:4318"));
        OtlpExporterPlan.Resolve(OtlpExporterMode.WhenEndpointConfigured, configuration).Registered.Should().BeFalse();
    }

    [Fact]
    public void AnEndpointInConfiguration_IsReadByTheExporter_AndByTheDetector()
    {
        // In-memory configuration, not an environment variable: the exporter reads IConfiguration, so the detector must too.
        var builder = NewBuilder((OtlpEndpointDetector.EndpointKey, "http://collector.example:4318"));
        builder.AddSharedKernelTelemetry("svc");
        using var host = builder.Build();

        OtlpEndpointDetector.IsConfigured(host.Services.GetRequiredService<IConfiguration>()).Should().BeTrue();
        ExporterEndpoint(host).Should().Be(new Uri("http://collector.example:4318"));
    }

    [Fact]
    public async Task DefaultMode_WithAnEndpoint_RegistersTheSameExportersAs1_0_0_AndTheBaggageProcessorRunsFirst()
    {
        await using var collector = await OtlpCollector.StartAsync();
        var builder = NewBuilder((OtlpEndpointDetector.EndpointKey, collector.Url));
        builder.AddSharedKernelTelemetry("svc");
        using var host = builder.Build();
        await host.StartAsync();

        SinglePlan(host).Registered.Should().BeTrue();
        ExportAllSignals(host, collector, correlationId: "corr-otlp-order-7731").Should().BeEquivalentTo(AllSignals);

        // The baggage processor is ahead of the log exporter: the exported record already carries the correlation id.
        collector.BodiesOf(Logs)
            .Should().Contain(body => Encoding.UTF8.GetString(body).Contains("corr-otlp-order-7731", StringComparison.Ordinal));
        await host.StopAsync();
    }

    [Fact]
    public void DefaultMode_WithNoEndpoint_StillRegistersTheExporter()
    {
        var builder = NewBuilder();
        builder.AddSharedKernelTelemetry("svc");
        using var host = builder.Build();

        SinglePlan(host).Registered.Should().BeTrue();
        host.Services.GetRequiredService<IOptions<SharedKernelTelemetryOptions>>().Value.OtlpExporter
            .Should().Be(OtlpExporterMode.Always);
    }

    [Fact]
    public async Task AnAspireStyleHost_EndpointSet_IsUnchangedInEitherMode()
    {
        foreach (var mode in new[] { OtlpExporterMode.Always, OtlpExporterMode.WhenEndpointConfigured })
        {
            await using var collector = await OtlpCollector.StartAsync();
            var builder = NewBuilder((OtlpEndpointDetector.EndpointKey, collector.Url));
            builder.AddServiceDefaults(o => o.OtlpExporter = mode);
            using var host = builder.Build();
            await host.StartAsync();

            ExportAllSignals(host, collector).Should().BeEquivalentTo(AllSignals, because: mode.ToString());
            await host.StopAsync();
        }
    }

    // ---- 13002 -------------------------------------------------------------------------------------------------------

    private static async Task<(IReadOnlyList<TestLogRecord> Records, InMemoryLoggerFactory Factory)> StartAndReadLogsAsync(HostApplicationBuilder builder)
    {
        builder.Services.RemoveAll<ILoggerFactory>();
        builder.Services.AddInMemoryLoggerFactory();
        using var host = builder.Build();
        await host.StartAsync();
        var factory = (InMemoryLoggerFactory)host.Services.GetRequiredService<ILoggerFactory>();
        await host.StopAsync();
        return ([.. factory.Loggers.Values.SelectMany(logger => logger.Records)], factory);
    }

    [Fact]
    public async Task DefaultMode_WithNoEndpoint_WarnsOnce_WithTheKernelCategoryAndConvention()
    {
        var builder = NewBuilder();
        builder.AddSharedKernelTelemetry("svc");

        var (records, factory) = await StartAndReadLogsAsync(builder);

        var record = records.ShouldHaveLogged(13002, LogLevel.Warning);
        records.Count(r => r.EventId.Id == 13002).Should().Be(1);
        record.Message.Should().Contain("OTEL_EXPORTER_OTLP_ENDPOINT").And.Contain("SharedKernel:ServiceDefaults:Telemetry:OtlpExporter");
        var own = records.Where(r => r.EventId.Id is >= 13000 and < 13100).ToList();
        own.ShouldFollowLogConvention();
        own.ShouldOnlyLogEventIdsInSubBlock(LoggingEventIdRanges.ServiceDefaults, 0);
        factory.ShouldHaveCategory(typeof(ServiceDefaultsLogCategory));
    }

    [Fact]
    public async Task ASecondAddSharedKernelTelemetry_DoesNotDoubleTheWarning()
    {
        var builder = NewBuilder();
        builder.AddSharedKernelTelemetry("svc");
        builder.AddSharedKernelTelemetry("svc");

        var (records, _) = await StartAndReadLogsAsync(builder);

        records.Count(r => r.EventId.Id == 13002).Should().Be(1);
    }

    [Fact]
    public async Task NoWarning_WhenTheEndpointIsSet()
    {
        var builder = NewBuilder((OtlpEndpointDetector.EndpointKey, "http://collector.example:4318"));
        builder.AddSharedKernelTelemetry("svc");

        var (records, _) = await StartAndReadLogsAsync(builder);

        records.ShouldNotHaveLogged(13002);
    }

    [Fact]
    public async Task NoWarning_InWhenEndpointConfiguredMode()
    {
        var builder = NewBuilder();
        builder.AddSharedKernelTelemetry("svc", o => o.OtlpExporter = OtlpExporterMode.WhenEndpointConfigured);

        var (records, _) = await StartAndReadLogsAsync(builder);

        records.ShouldNotHaveLogged(13002);
    }

    // ---- options -----------------------------------------------------------------------------------------------------

    [Fact]
    public void TheOptions_BindFromTheSection()
    {
        var builder = NewBuilder(($"{SharedKernelTelemetryOptions.SectionName}:OtlpExporter", "WhenEndpointConfigured"));
        builder.AddSharedKernelTelemetry("svc");
        using var host = builder.Build();

        host.Services.GetRequiredService<IOptions<SharedKernelTelemetryOptions>>().Value.OtlpExporter
            .Should().Be(OtlpExporterMode.WhenEndpointConfigured);
        SinglePlan(host).Registered.Should().BeFalse("the section selected WhenEndpointConfigured and no endpoint is set");
    }

    [Fact]
    public void TheOptions_BindFromTheEnvironmentForm()
    {
        const string Variable = "SharedKernel__ServiceDefaults__Telemetry__OtlpExporter";
        Environment.SetEnvironmentVariable(Variable, "WhenEndpointConfigured");
        try
        {
            var builder = NewBuilder();
            builder.AddSharedKernelTelemetry("svc");
            using var host = builder.Build();

            host.Services.GetRequiredService<IOptions<SharedKernelTelemetryOptions>>().Value.OtlpExporter
                .Should().Be(OtlpExporterMode.WhenEndpointConfigured);
            SinglePlan(host).Registered.Should().BeFalse();
        }
        finally
        {
            Environment.SetEnvironmentVariable(Variable, null);
        }
    }

    [Fact]
    public void TheConfigureDelegate_OverridesConfiguration()
    {
        var builder = NewBuilder(($"{SharedKernelTelemetryOptions.SectionName}:OtlpExporter", "WhenEndpointConfigured"));
        builder.AddServiceDefaults(o => o.OtlpExporter = OtlpExporterMode.Always);
        using var host = builder.Build();

        host.Services.GetRequiredService<IOptions<SharedKernelTelemetryOptions>>().Value.OtlpExporter
            .Should().Be(OtlpExporterMode.Always);
        SinglePlan(host).Registered.Should().BeTrue();
    }

    [Fact]
    public async Task AnUndefinedMode_FailsValidationOnStart_NamingTheSetting()
    {
        var builder = NewBuilder(($"{SharedKernelTelemetryOptions.SectionName}:OtlpExporter", "99"));
        builder.AddSharedKernelTelemetry("svc");
        using var host = builder.Build();

        var thrown = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        thrown.Message.Should().Contain("SharedKernel:ServiceDefaults:Telemetry:OtlpExporter");
    }

    [Fact]
    public void TheModeDefaultsToAlways_AndTheZeroValueIsAlways()
    {
        new SharedKernelTelemetryOptions().OtlpExporter.Should().Be(OtlpExporterMode.Always);
        ((int)OtlpExporterMode.Always).Should().Be(0);
        SharedKernelTelemetryOptions.SectionName.Should().Be("SharedKernel:ServiceDefaults:Telemetry");
    }
}
