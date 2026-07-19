using System.Diagnostics;
using System.Diagnostics.Metrics;
using SharedKernel.Search.Abstractions.Constants;

namespace SharedKernel.Search.ElasticSearch.Diagnostics;

/// <summary>
/// This package's own <see cref="ActivitySource"/>/<see cref="Meter"/> instances, named from
/// <see cref="SearchWellKnown.ActivitySourceName"/>/<see cref="SearchWellKnown.MeterName"/>.
/// </summary>
/// <remarks>
/// The Meilisearch provider declares its own, separate instance under the same string names — that
/// byte-identical naming, not a shared type, is what lets <c>13.ServiceDefaults</c> wire one
/// <c>WithSearchTelemetry()</c> call covering both providers with no <c>ProjectReference</c> to
/// <c>09.Search</c>. Siblings never share a type; the OpenTelemetry SDK no-ops on a repeated
/// source/meter name.
/// </remarks>
internal static class SearchDiagnostics
{
    /// <summary>Gets this package's <see cref="ActivitySource"/>.</summary>
    public static ActivitySource ActivitySource { get; } = new(SearchWellKnown.ActivitySourceName, "1.0.0");

    /// <summary>Gets this package's <see cref="Meter"/>.</summary>
    public static Meter Meter { get; } = new(SearchWellKnown.MeterName, "1.0.0");
}
