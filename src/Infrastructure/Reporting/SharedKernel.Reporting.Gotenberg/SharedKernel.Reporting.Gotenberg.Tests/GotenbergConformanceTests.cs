using System.Diagnostics;
using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharedKernel.Execution.Context;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Logging;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Primitives.Results;
using SharedKernel.Testing.Logging;
using SharedKernel.Testing.Telemetry;
using Xunit;

namespace SharedKernel.Reporting.Gotenberg.Tests;

/// <summary>Correlation propagation, telemetry and logging of the Gotenberg converter against a stub Gotenberg.</summary>
public sealed class GotenbergConformanceTests
{
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n%stub\n%%EOF\n");

    [Fact]
    public async Task OnlyTheCorrelationId_LeavesThePlatform_AsGotenbergTrace()
    {
        var handler = new StubHandler(_ => Ok());
        var context = new SystemRequestContext(["reports.read"], "actor-7", new TenantId(Guid.NewGuid()), "corr-42");
        using IHost host = Host(handler, services => services.AddSingleton<IRequestContextAccessor>(new Accessor(context)));

        (await Converter(host).ConvertToStreamAsync("<p/>", Stream.Null)).IsSuccess.Should().BeTrue();

        StubHandler.Captured request = handler.Requests.Single();
        request.Headers["Gotenberg-Trace"].Should().Be("corr-42");
        request.Headers.Keys.Should().NotContain(
            [WellKnownHeaders.CorrelationId, WellKnownHeaders.TenantId, WellKnownHeaders.ActorId, WellKnownHeaders.ActorKind, WellKnownHeaders.ClientId],
            "the converter is a third-party process rendering caller-supplied HTML");
    }

    [Fact]
    public async Task WithoutAContext_NoTraceHeaderIsSent()
    {
        var handler = new StubHandler(_ => Ok());
        using IHost host = Host(handler);

        (await Converter(host).ConvertToStreamAsync("<p/>", Stream.Null)).IsSuccess.Should().BeTrue();

        handler.Requests.Single().Headers.Should().NotContainKey("Gotenberg-Trace");
    }

    [Theory]
    [InlineData("bad value with spaces")]
    [InlineData("a/b")]
    public async Task AnInvalidCorrelationId_IsNotSent(string correlationId)
    {
        var handler = new StubHandler(_ => Ok());
        var context = new SystemRequestContext([], "actor-7", correlationId: correlationId);
        using IHost host = Host(handler, services => services.AddSingleton<IRequestContextAccessor>(new Accessor(context)));

        (await Converter(host).ConvertToStreamAsync("<p/>", Stream.Null)).IsSuccess.Should().BeTrue();

        handler.Requests.Single().Headers.Should().NotContainKey("Gotenberg-Trace");
    }

    [Fact]
    public async Task Convert_RecordsTheSameSpanAndInstrumentsAsEveryProvider()
    {
        using var spans = ActivityRecorder.StartRecording(ReportingDiagnostics.ActivitySourceName);
        using var metrics = MetricRecorder.StartRecording(ReportingDiagnostics.MeterName);
        var handler = new StubHandler(_ => Ok());
        using IHost host = Host(handler);

        Result<long> result = await Converter(host).ConvertToStreamAsync("<p/>", Stream.Null);

        result.IsSuccess.Should().BeTrue();
        spans.RecordedActivities.ShouldHaveSingleSpan("reporting convert")
            .ShouldHaveKind(ActivityKind.Internal)
            .ShouldHaveTag("reporting.format", "pdf")
            .ShouldHaveTag("reporting.operation", "convert")
            .ShouldHaveTag("reporting.size_bytes", (long)Pdf.Length)
            .ShouldBeRecordedSuccess();
        metrics
            .ShouldHaveInstrument("sharedkernel.reporting.operation.duration", "s", InstrumentKind.Histogram)
            .ShouldHaveInstrument("sharedkernel.reporting.bytes", "By", InstrumentKind.Counter)
            .ShouldHaveSum("sharedkernel.reporting.bytes", Pdf.Length)
            .ShouldHaveMeasurement(
                "sharedkernel.reporting.operation.duration",
                null,
                new KeyValuePair<string, object?>("outcome", WellKnownOutcomes.Success));
    }

    [Fact]
    public async Task AConverterFailure_IsRecordedWithItsErrorTypeAndCode()
    {
        using var spans = ActivityRecorder.StartRecording(ReportingDiagnostics.ActivitySourceName);
        using var metrics = MetricRecorder.StartRecording(ReportingDiagnostics.MeterName);
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway));
        using IHost host = Host(handler);

        Result<long> result = await Converter(host).ConvertToStreamAsync("<p/>", Stream.Null);

        result.Error.Code.Should().Be(ReportingErrorCodes.ConverterUnavailable);
        spans.RecordedActivities.ShouldHaveSingleSpan("reporting convert").ShouldBeRecordedFailure(result.Error);
        metrics.ShouldHaveMeasurement(
            "sharedkernel.reporting.operation.duration",
            null,
            new KeyValuePair<string, object?>("outcome", WellKnownOutcomes.Failure),
            new KeyValuePair<string, object?>("error.type", nameof(ErrorType.Unavailable)));
    }

    [Fact]
    public async Task TheConversionPipelineLogs_InTheAbstractionsSubBlock_UnderTheDependenciesCategory()
    {
        var logger = new InMemoryLogger<ReportingDependencies>();
        var handler = new StubHandler(_ => Ok());
        using IHost host = Host(handler, services => services.AddSingleton<ILogger<ReportingDependencies>>(logger));

        await Converter(host).ConvertToStreamAsync("<h1>secret-document</h1>", Stream.Null);

        logger.Records.ShouldHaveLogged(new EventId(20006), LogLevel.Debug);
        logger.Records
            .ShouldOnlyLogEventIdsInSubBlock(LoggingEventIdRanges.Reporting, 0)
            .ShouldNotLeak("secret-document", "gotenberg.test")
            .ShouldFollowLogConvention();
    }

    private static HttpResponseMessage Ok() => new(HttpStatusCode.OK) { Content = new ByteArrayContent(Pdf) };

    private static IHtmlToPdfConverter Converter(IHost host) => host.Services.GetRequiredService<IHtmlToPdfConverter>();

    private static IHost Host(StubHandler handler, Action<IServiceCollection>? configure = null)
    {
        HostApplicationBuilder builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SharedKernel:Reporting:Gotenberg:BaseUrl"] = "http://gotenberg.test",
        });
        builder.Services.AddSharedKernelReporting().AddGotenberg(builder.Configuration);
        builder.Services.AddHttpClient(GotenbergHtmlToPdfConverter.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => handler);
        configure?.Invoke(builder.Services);
        return builder.Build();
    }

    private sealed class Accessor(IRequestContext context) : IRequestContextAccessor
    {
        public IRequestContext? Current { get; } = context;
    }
}
