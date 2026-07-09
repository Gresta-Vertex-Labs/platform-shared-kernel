using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenTelemetry.Logs;
using SharedKernel.ServiceDefaults.Telemetry;

namespace SharedKernel.ServiceDefaults.Tests.Telemetry;

public sealed class TelemetryExtensionsTests
{
    [Fact]
    public void AddSharedKernelTelemetry_DoesNotThrow()
    {
        var builder = WebApplication.CreateBuilder();

        var exception = Record.Exception(() => builder.AddSharedKernelTelemetry("test-service"));

        Assert.Null(exception);
    }

    [Fact]
    public void AddSharedKernelTelemetry_LoggingExport_SetsIncludeScopesAndIncludeFormattedMessage()
    {
        var builder = WebApplication.CreateBuilder();
        builder.AddSharedKernelTelemetry("test-service");

        using var provider = builder.Services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<OpenTelemetryLoggerOptions>>().Value;

        Assert.True(options.IncludeScopes);
        Assert.True(options.IncludeFormattedMessage);
    }

    [Fact]
    public void AddSharedKernelTelemetry_RegistersBaggageLogRecordProcessorExactlyOnce()
    {
        var builder = WebApplication.CreateBuilder();
        builder.AddSharedKernelTelemetry("test-service");

        using var provider = builder.Services.BuildServiceProvider();
        var processors = provider.GetServices<BaggageLogRecordProcessor>().ToList();

        Assert.Single(processors);
    }
}
