using Microsoft.AspNetCore.Builder;
using SharedKernel.ServiceDefaults.Telemetry;
using Temporalio.Extensions.OpenTelemetry;

namespace SharedKernel.ServiceDefaults.Tests.Telemetry;

/// <summary>
/// <c>WithWorkflowTelemetry()</c> against the Temporal SDK's own source objects, so an upstream rename fails here
/// instead of silently dropping the workflow spans.
/// </summary>
[Collection(NonParallelTelemetryCollection.Name)]
public sealed class WorkflowTelemetryExtensionsTests
{
    private const string TemporalPrefix = "Temporalio.Extensions.OpenTelemetry.";

    private static readonly string[] SdkSources =
    [
        TracingInterceptor.ClientSource.Name,
        TracingInterceptor.WorkflowsSource.Name,
        TracingInterceptor.ActivitiesSource.Name,
        TracingInterceptor.NexusSource.Name,
    ];

    public static TheoryData<string> SdkSourceNames => new(SdkSources);

    [Fact]
    public void WithWorkflowTelemetry_ReturnsSameBuilderInstance()
    {
        var builder = WebApplication.CreateBuilder();

        Assert.Same(builder, builder.WithWorkflowTelemetry());
    }

    [Fact]
    public void TheSdksTracingSources_AllShareTheSubscribedPrefix()
    {
        Assert.All(SdkSources, name => Assert.StartsWith(TemporalPrefix, name, StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(SdkSourceNames))]
    public void WithWorkflowTelemetry_ExportsASpanFromEachTemporalTracingSource(string sourceName)
    {
        using var host = TelemetryHarness.Create(builder => builder.WithWorkflowTelemetry());

        TelemetryHarness.EmitSpan(sourceName, "StartWorkflow:Test");

        Assert.Single(host.Spans, a => a.Source.Name == sourceName);
    }

    [Fact]
    public void WithWorkflowTelemetry_DoesNotExportASourceOutsideTheTemporalPrefix()
    {
        using var host = TelemetryHarness.Create(builder => builder.WithWorkflowTelemetry());

        TelemetryHarness.EmitSpan("Temporalio");
        TelemetryHarness.EmitSpan("Other.Temporalio.Extensions.OpenTelemetry.Client");
        TelemetryHarness.EmitSpan("SharedKernel.Workflows");

        Assert.Empty(host.Spans);
    }

    [Fact]
    public void WithWorkflowTelemetry_CalledTwice_AddsNoDuplicate()
    {
        using var host = TelemetryHarness.Create(builder =>
        {
            builder.WithWorkflowTelemetry();
            builder.WithWorkflowTelemetry();
        });

        TelemetryHarness.EmitSpan(TracingInterceptor.ClientSource.Name);

        Assert.Single(host.Spans);
    }
}
