using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using SharedKernel.ServiceDefaults.Extensions;
using SharedKernel.ServiceDefaults.Telemetry;

namespace SharedKernel.ServiceDefaults.Tests.Telemetry;

/// <summary>
/// The second-scale view: a <c>Histogram&lt;double&gt;</c> with unit <c>s</c> in a <c>SharedKernel.*</c> meter gets the
/// semantic-convention boundaries; every other histogram keeps the SDK defaults; one metric stream per instrument.
/// </summary>
[Collection(NonParallelTelemetryCollection.Name)]
public sealed class DurationHistogramViewTests
{
    private static readonly double[] SecondsBoundaries =
        [0.005, 0.01, 0.025, 0.05, 0.075, 0.1, 0.25, 0.5, 0.75, 1, 2.5, 5, 7.5, 10];

    private static readonly double[] SdkDefaultBoundaries =
        [0, 5, 10, 25, 50, 75, 100, 250, 500, 750, 1000, 2500, 5000, 7500, 10000];

    [Fact]
    public void SecondsHistogram_InAKernelMeter_GetsTheSecondScaleBoundaries()
    {
        using var host = TelemetryHarness.ForServiceDefaults();
        using var meter = new Meter("SharedKernel.ViewKernelSeconds");
        meter.CreateHistogram<double>("view.kernel.seconds", unit: "s").Record(0.03);

        host.Flush();

        Assert.Equal([.. SecondsBoundaries, double.PositiveInfinity], BoundsOf(host, "view.kernel.seconds"));
    }

    [Fact]
    public void SecondsHistogram_InAnotherVendorsMeter_KeepsTheDefaults()
    {
        // A non-kernel meter is not subscribed by the wildcard; subscribe it explicitly to observe its stream.
        using var host = TelemetryHarness.Create(builder =>
        {
            builder.AddServiceDefaults();
            builder.Services.AddOpenTelemetry().WithMetrics(m => m.AddMeter("Other.ViewVendorSeconds"));
        });
        using var meter = new Meter("Other.ViewVendorSeconds");
        meter.CreateHistogram<double>("view.vendor.seconds", unit: "s").Record(0.03);

        host.Flush();

        Assert.Equal([.. SdkDefaultBoundaries, double.PositiveInfinity], BoundsOf(host, "view.vendor.seconds"));
    }

    [Fact]
    public void MillisecondHistogram_InAKernelMeter_KeepsTheDefaults()
    {
        using var host = TelemetryHarness.ForServiceDefaults();
        using var meter = new Meter("SharedKernel.ViewKernelMilliseconds");
        meter.CreateHistogram<double>("view.kernel.milliseconds", unit: "ms").Record(30);

        host.Flush();

        Assert.Equal([.. SdkDefaultBoundaries, double.PositiveInfinity], BoundsOf(host, "view.kernel.milliseconds"));
    }

    [Fact]
    public void LongHistogram_InAKernelMeter_KeepsTheDefaults()
    {
        using var host = TelemetryHarness.ForServiceDefaults();
        using var meter = new Meter("SharedKernel.ViewKernelLong");
        meter.CreateHistogram<long>("view.kernel.long", unit: "s").Record(3);

        host.Flush();

        Assert.Equal([.. SdkDefaultBoundaries, double.PositiveInfinity], BoundsOf(host, "view.kernel.long"));
    }

    [Fact]
    public void AddServiceDefaults_ThenAddSharedKernelTelemetryAgain_YieldsOneMetricStream()
    {
        using var host = TelemetryHarness.Create(builder =>
        {
            builder.AddServiceDefaults();
            builder.AddSharedKernelTelemetry("second-call");
        });
        using var meter = new Meter("SharedKernel.ViewTwice");
        meter.CreateHistogram<double>("view.twice.seconds", unit: "s").Record(0.2);

        host.Flush();

        Assert.Single(host.Metrics, m => m.Name == "view.twice.seconds");
    }

    private static double[] BoundsOf(TelemetryHarness host, string instrumentName)
    {
        var snapshot = Assert.Single(host.Metrics, m => m.Name == instrumentName);
        var bounds = new List<double>();
        foreach (var point in snapshot.MetricPoints)
        {
            foreach (var bucket in point.GetHistogramBuckets())
                bounds.Add(bucket.ExplicitBound);
        }

        return [.. bounds.Distinct()];
    }
}
