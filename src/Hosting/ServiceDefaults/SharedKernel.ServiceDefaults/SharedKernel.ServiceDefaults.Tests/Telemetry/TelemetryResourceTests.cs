using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using SharedKernel.Primitives.Telemetry;
using SharedKernel.ServiceDefaults.Telemetry;

namespace SharedKernel.ServiceDefaults.Tests.Telemetry;

/// <summary>
/// The resource <c>AddSharedKernelTelemetry</c> configures: one value across the tracer, meter and logger providers,
/// <c>service.version</c> from the entry assembly's informational version, and the environment-variable precedence
/// of the OpenTelemetry resource detectors left as it was.
/// </summary>
[Collection(NonParallelTelemetryCollection.Name)]
public sealed class TelemetryResourceTests
{
    private const string ServiceNameKey = "service.name";
    private const string ServiceVersionKey = "service.version";
    private const string NameVariable = "OTEL_SERVICE_NAME";
    private const string AttributesVariable = "OTEL_RESOURCE_ATTRIBUTES";

    [Fact]
    public void ServiceName_IsTheSuppliedName_OnAllThreeProviders()
    {
        using var scope = new EnvironmentScope((NameVariable, null), (AttributesVariable, null));

        foreach (var resource in BuildResources("orders-api"))
            Assert.Equal("orders-api", Attribute(resource, ServiceNameKey));
    }

    [Fact]
    public void ServiceVersion_IsTheEntryAssemblysInformationalVersion_AndIdenticalOnAllThreeProviders()
    {
        using var scope = new EnvironmentScope((NameVariable, null), (AttributesVariable, null));
        var entry = Assembly.GetEntryAssembly();
        var expected = TelemetryVersion.Of(entry!);

        var versions = BuildResources("orders-api").Select(r => Attribute(r, ServiceVersionKey)).ToList();

        Assert.All(versions, v => Assert.Equal(expected, v));
    }

    [Fact]
    public void ServiceVersion_KeepsBuildMetadata_AsTelemetryVersionReturnsIt()
    {
        var entry = Assembly.GetEntryAssembly()!;
        var informational = entry.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        Assert.Equal(informational ?? TelemetryVersion.Of(entry), TelemetryExtensions.ResolveServiceVersion(entry));
    }

    [Fact]
    public void ResolveServiceVersion_WithoutAnEntryAssembly_IsUnknown_NeverTheKernelsOwnVersion()
    {
        Assert.Equal(TelemetryVersion.Unknown, TelemetryExtensions.ResolveServiceVersion(null));
        Assert.Equal("0.0.0", TelemetryVersion.Unknown);
    }

    [Fact]
    public void EnvironmentVariables_KeepTheirPrecedenceOverTheDefaults_AndTheSuppliedNameWins()
    {
        // Characterised before the single ConfigureResource replaced the three SetResourceBuilder calls:
        // the explicit name beats OTEL_SERVICE_NAME, and OTEL_RESOURCE_ATTRIBUTES adds its own attributes.
        using var scope = new EnvironmentScope(
            (NameVariable, "from-environment"),
            (AttributesVariable, "deployment.environment=staging,team.name=platform"));

        foreach (var resource in BuildResources("orders-api"))
        {
            Assert.Equal("orders-api", Attribute(resource, ServiceNameKey));
            Assert.Equal("staging", Attribute(resource, "deployment.environment"));
            Assert.Equal("platform", Attribute(resource, "team.name"));
        }
    }

    private static List<Resource> BuildResources(string serviceName)
    {
        var builder = WebApplication.CreateBuilder();
        builder.AddSharedKernelTelemetry(serviceName);

        using var provider = builder.Services.BuildServiceProvider();
        return
        [
            provider.GetRequiredService<TracerProvider>().GetResource(),
            provider.GetRequiredService<MeterProvider>().GetResource(),
            provider.GetRequiredService<LoggerProvider>().GetResource(),
        ];
    }

    private static object? Attribute(Resource resource, string key) =>
        resource.Attributes.FirstOrDefault(a => a.Key == key).Value;

    private sealed class EnvironmentScope : IDisposable
    {
        private readonly (string Name, string? Previous)[] _previous;

        public EnvironmentScope(params (string Name, string? Value)[] variables)
        {
            _previous = [.. variables.Select(v => (v.Name, Environment.GetEnvironmentVariable(v.Name)))];
            foreach (var (name, value) in variables)
                Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose()
        {
            foreach (var (name, previous) in _previous)
                Environment.SetEnvironmentVariable(name, previous);
        }
    }
}
