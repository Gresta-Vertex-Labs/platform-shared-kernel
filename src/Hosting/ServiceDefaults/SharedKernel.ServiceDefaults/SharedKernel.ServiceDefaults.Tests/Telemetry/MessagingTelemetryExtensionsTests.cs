using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Builder;
using SharedKernel.ServiceDefaults.Telemetry;

namespace SharedKernel.ServiceDefaults.Tests.Telemetry;

[Collection(NonParallelTelemetryCollection.Name)]
public sealed class MessagingTelemetryExtensionsTests
{
    [Fact]
    public void WithMessagingTelemetry_ReturnsSameBuilderInstance()
    {
        var builder = WebApplication.CreateBuilder();

        Assert.Same(builder, builder.WithMessagingTelemetry());
    }

    [Fact]
    public void WithMessagingTelemetry_ExportsASpanFromTheMassTransitSource()
    {
        using var host = TelemetryHarness.Create(builder => builder.WithMessagingTelemetry());

        TelemetryHarness.EmitSpan("MassTransit", "Send");

        Assert.Single(host.Spans, a => a.Source.Name == "MassTransit");
    }

    [Fact]
    public void WithMessagingTelemetry_ExportsAMeasurementFromTheMassTransitMeter()
    {
        using var host = TelemetryHarness.Create(builder => builder.WithMessagingTelemetry());

        using var meter = new Meter("MassTransit");
        meter.CreateCounter<long>("messaging.test.count").Add(1);
        host.Flush();

        Assert.Single(host.Metrics, m => m.MeterName == "MassTransit" && m.Name == "messaging.test.count");
    }

    [Fact]
    public void WithMessagingTelemetry_CalledTwice_AddsNoDuplicate()
    {
        using var host = TelemetryHarness.Create(builder =>
        {
            builder.WithMessagingTelemetry();
            builder.WithMessagingTelemetry();
        });

        TelemetryHarness.EmitSpan("MassTransit", "Send");
        using var meter = new Meter("MassTransit");
        meter.CreateCounter<long>("messaging.twice.count").Add(1);
        host.Flush();

        Assert.Single(host.Spans, a => a.Source.Name == "MassTransit");
        Assert.Single(host.Metrics, m => m.Name == "messaging.twice.count");
    }

    [Fact]
    public void WithMessagingTelemetry_SubscribesNoKernelName_TheWildcardOwnsThose()
    {
        using var host = TelemetryHarness.Create(builder => builder.WithMessagingTelemetry());

        TelemetryHarness.EmitSpan("SharedKernel.Messaging");

        Assert.DoesNotContain("SharedKernel.Messaging", host.SpanSourceNames);
    }
}
