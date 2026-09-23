using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Search.Abstractions.Constants;

namespace SharedKernel.Search.Meilisearch.Diagnostics;

/// <summary>
/// This package's own <see cref="ActivitySource"/>/<see cref="Meter"/> instances and the helpers that
/// record to them, named from <see cref="SearchWellKnown.ActivitySourceName"/>/
/// <see cref="SearchWellKnown.MeterName"/>.
/// </summary>
/// <remarks>
/// <para>
/// The ElasticSearch provider declares its own, separate instance under the same string names — that
/// byte-identical naming, not a shared type, is what lets <c>13.ServiceDefaults</c> wire one
/// <c>WithSearchTelemetry()</c> call covering both providers with no <c>ProjectReference</c> to
/// <c>09.Search</c>. Both providers emit the same span names, the same tag keys and the same two
/// instruments, so a dashboard built against one provider keeps working after a swap.
/// </para>
/// <para>
/// <b>Query text is never recorded.</b> Free text is user input and routinely carries personal data —
/// a customer's own name, email or order reference typed into a search box. Spans and measurements
/// carry the index, the operation, the provider and, on failure, the error code; never the query, never
/// a filter value, never a document id.
/// </para>
/// </remarks>
internal static class SearchDiagnostics
{
    private static readonly string Version =
        typeof(SearchDiagnostics).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
        ?? "0.0.0";

    /// <summary>Gets this package's <see cref="ActivitySource"/>.</summary>
    public static ActivitySource ActivitySource { get; } = new(SearchWellKnown.ActivitySourceName, Version);

    /// <summary>Gets this package's <see cref="Meter"/>.</summary>
    public static Meter Meter { get; } = new(SearchWellKnown.MeterName, Version);

    private static Histogram<double> Duration { get; } = Meter.CreateHistogram<double>(
        SearchWellKnown.OperationDurationMetricName,
        unit: "s",
        description: "Duration of search operations, by index, operation, provider and outcome.");

    private static Counter<long> Documents { get; } = Meter.CreateCounter<long>(
        SearchWellKnown.DocumentsMetricName,
        unit: "{document}",
        description: "Documents a write or delete operation was asked to affect, by index and operation.");

    /// <summary>
    /// Starts a client span named <c>search {operation}</c> for <paramref name="operation"/> against
    /// <paramref name="indexName"/>, or returns <see langword="null"/> when nothing is listening.
    /// </summary>
    public static Activity? StartActivity(string operation, string indexName)
    {
        var activity = ActivitySource.StartActivity($"search {operation}", ActivityKind.Client);
        if (activity is not null)
        {
            activity.SetTag(SearchWellKnown.IndexTagName, indexName);
            activity.SetTag(SearchWellKnown.OperationTagName, operation);
            activity.SetTag(SearchWellKnown.ProviderTagName, SearchWellKnown.MeilisearchProviderName);
        }

        return activity;
    }

    /// <summary>
    /// Records the duration of <paramref name="operation"/> and closes out
    /// <paramref name="activity"/>, marking it failed when <paramref name="errorCode"/> is non-null.
    /// </summary>
    public static void Complete(Activity? activity, string operation, string indexName, long startTimestamp, string? errorCode)
    {
        var tags = new TagList
        {
            { SearchWellKnown.IndexTagName, indexName },
            { SearchWellKnown.OperationTagName, operation },
            { SearchWellKnown.ProviderTagName, SearchWellKnown.MeilisearchProviderName },
        };

        if (errorCode is not null)
        {
            tags.Add(WellKnownTagKeys.ErrorType, errorCode);
            activity?.SetTag(WellKnownTagKeys.ErrorType, errorCode);
            activity?.SetStatus(ActivityStatusCode.Error, errorCode);
        }

        Duration.Record(Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds, tags);
    }

    /// <summary>Records that <paramref name="count"/> documents were submitted to <paramref name="operation"/>.</summary>
    public static void RecordDocuments(string operation, string indexName, long count)
    {
        if (count <= 0)
        {
            return;
        }

        Documents.Add(
            count,
            new TagList
            {
                { SearchWellKnown.IndexTagName, indexName },
                { SearchWellKnown.OperationTagName, operation },
                { SearchWellKnown.ProviderTagName, SearchWellKnown.MeilisearchProviderName },
            });
    }
}
