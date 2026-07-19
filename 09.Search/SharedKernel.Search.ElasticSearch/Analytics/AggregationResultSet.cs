using System.Diagnostics.CodeAnalysis;

namespace SharedKernel.Search.ElasticSearch.Analytics;

/// <summary>
/// The name-keyed result of <see cref="IAnalyticsSearch{TDocument}.AggregateAsync"/>, read back via
/// type-specific <c>TryGet*</c> accessors.
/// </summary>
/// <remarks>
/// The <c>TryGet*</c> accessors mirror the ElasticSearch client's own name-keyed, type-specific read
/// model rather than pretending one generic accessor can preserve bucket type. They return
/// <see cref="bool"/>, not <c>Result</c> — "I asked for a terms aggregation and read it as terms" is a
/// caller programming error surfaced at first test run, not an expected runtime failure.
/// </remarks>
public sealed record AggregationResultSet
{
    /// <summary>Gets every aggregation result, keyed by the name it was requested under.</summary>
    public required IReadOnlyDictionary<string, AggregationResult> Results { get; init; }

    /// <summary>Attempts to read the aggregation named <paramref name="name"/> as a <see cref="TermsResult"/>.</summary>
    public bool TryGetTerms(string name, [NotNullWhen(true)] out TermsResult? result)
        => TryGet(name, out result);

    /// <summary>Attempts to read the aggregation named <paramref name="name"/> as a <see cref="CardinalityResult"/>.</summary>
    public bool TryGetCardinality(string name, [NotNullWhen(true)] out CardinalityResult? result)
        => TryGet(name, out result);

    /// <summary>Attempts to read the aggregation named <paramref name="name"/> as a <see cref="StatsResult"/>.</summary>
    public bool TryGetStats(string name, [NotNullWhen(true)] out StatsResult? result)
        => TryGet(name, out result);

    /// <summary>Attempts to read the aggregation named <paramref name="name"/> as a <see cref="DateHistogramResult"/>.</summary>
    public bool TryGetDateHistogram(string name, [NotNullWhen(true)] out DateHistogramResult? result)
        => TryGet(name, out result);

    /// <summary>Attempts to read the aggregation named <paramref name="name"/> as a <see cref="RangeResult"/>.</summary>
    public bool TryGetRange(string name, [NotNullWhen(true)] out RangeResult? result)
        => TryGet(name, out result);

    private bool TryGet<TResult>(string name, [NotNullWhen(true)] out TResult? result)
        where TResult : AggregationResult
    {
        if (Results.TryGetValue(name, out var found) && found is TResult typed)
        {
            result = typed;
            return true;
        }

        result = null;
        return false;
    }
}
