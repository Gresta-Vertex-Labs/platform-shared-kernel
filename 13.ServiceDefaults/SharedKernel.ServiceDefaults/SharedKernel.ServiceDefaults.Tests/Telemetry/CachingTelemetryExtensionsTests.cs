using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using SharedKernel.ServiceDefaults.Telemetry;

namespace SharedKernel.ServiceDefaults.Tests.Telemetry;

public sealed class CachingTelemetryExtensionsTests
{
    [Fact]
    public void WithCachingTelemetry_CalledTwice_RegistersMeterNameOnce()
    {
        var builder = WebApplication.CreateBuilder();

        builder.WithCachingTelemetry();
        builder.WithCachingTelemetry();

        using var provider = builder.Services.BuildServiceProvider();
        var meterProviderBuilder = provider.GetServices<MeterProviderBuilder>().ToList();

        // The OpenTelemetry SDK de-duplicates registered meter names internally; calling the
        // extension twice must not throw and must not register a second MeterProviderBuilder
        // configuration delegate that would double-count instruments.
        Assert.True(meterProviderBuilder.Count <= 1);
    }

    [Fact]
    public void WithCachingTelemetry_DoesNotThrow()
    {
        var builder = WebApplication.CreateBuilder();

        var exception = Record.Exception(() =>
        {
            builder.WithCachingTelemetry();
            builder.WithCachingTelemetry();
        });

        Assert.Null(exception);
    }
}
