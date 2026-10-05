namespace SharedKernel.Search.ElasticSearch.Analytics;

/// <summary>The calendar interval a <see cref="DateHistogramAggregation"/> buckets by.</summary>
public enum DateHistogramInterval
{
    /// <summary>One-minute buckets.</summary>
    Minute = 0,

    /// <summary>One-hour buckets.</summary>
    Hour = 1,

    /// <summary>One-day buckets.</summary>
    Day = 2,

    /// <summary>One-week buckets.</summary>
    Week = 3,

    /// <summary>One-month buckets.</summary>
    Month = 4,

    /// <summary>One-quarter buckets.</summary>
    Quarter = 5,

    /// <summary>One-year buckets.</summary>
    Year = 6,
}
