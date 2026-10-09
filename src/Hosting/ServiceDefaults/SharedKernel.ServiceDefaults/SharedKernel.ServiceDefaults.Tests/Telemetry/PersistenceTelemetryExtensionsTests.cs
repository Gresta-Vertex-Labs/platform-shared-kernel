using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Builder;
using SharedKernel.ServiceDefaults.Telemetry;

namespace SharedKernel.ServiceDefaults.Tests.Telemetry;

[Collection(NonParallelTelemetryCollection.Name)]
public sealed class PersistenceTelemetryExtensionsTests
{
    [Fact]
    public void WithPersistenceTelemetry_ReturnsSameBuilderInstance()
    {
        var builder = WebApplication.CreateBuilder();

        Assert.Same(builder, builder.WithPersistenceTelemetry());
    }

    [Fact]
    public void WithPersistenceTelemetry_ExportsASpanFromTheNpgsqlSource()
    {
        using var host = TelemetryHarness.Create(builder => builder.WithPersistenceTelemetry());

        TelemetryHarness.EmitSpan("Npgsql", "SELECT");

        Assert.Single(host.Spans, a => a.Source.Name == "Npgsql");
    }

    [Fact]
    public void WithPersistenceTelemetry_ExportsAMeasurementFromTheNpgsqlMeter()
    {
        using var host = TelemetryHarness.Create(builder => builder.WithPersistenceTelemetry());

        using var meter = new Meter("Npgsql");
        meter.CreateCounter<long>("db.test.count").Add(1);
        host.Flush();

        Assert.Single(host.Metrics, m => m.MeterName == "Npgsql" && m.Name == "db.test.count");
    }

    [Fact]
    public void WithPersistenceTelemetry_CalledTwice_AddsNoDuplicate()
    {
        using var host = TelemetryHarness.Create(builder =>
        {
            builder.WithPersistenceTelemetry();
            builder.WithPersistenceTelemetry();
        });

        TelemetryHarness.EmitSpan("Npgsql", "SELECT");
        using var meter = new Meter("Npgsql");
        meter.CreateCounter<long>("db.twice.count").Add(1);
        host.Flush();

        Assert.Single(host.Spans, a => a.Source.Name == "Npgsql");
        Assert.Single(host.Metrics, m => m.Name == "db.twice.count");
    }

    [Fact]
    public void WithPersistenceTelemetry_SubscribesNoKernelName_TheWildcardOwnsThose()
    {
        using var host = TelemetryHarness.Create(builder => builder.WithPersistenceTelemetry());

        TelemetryHarness.EmitSpan("SharedKernel.Persistence");

        Assert.DoesNotContain("SharedKernel.Persistence", host.SpanSourceNames);
    }
}
