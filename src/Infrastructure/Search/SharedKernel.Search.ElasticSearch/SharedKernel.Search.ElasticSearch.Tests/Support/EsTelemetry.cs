using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Search.ElasticSearch.Diagnostics;

namespace SharedKernel.Search.ElasticSearch.Tests.Support;

/// <summary>Builds the provider telemetry over the host's default <see cref="IMeterFactory"/>, as the registration does.</summary>
internal static class EsTelemetry
{
    public static SearchTelemetry Create() =>
        new(new ServiceCollection().AddMetrics().BuildServiceProvider().GetRequiredService<IMeterFactory>());
}
