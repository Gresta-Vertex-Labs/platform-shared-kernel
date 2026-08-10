namespace SharedKernel.Search.Abstractions.Models;

/// <summary>
/// Opt-in backpressure controls for a bulk write (<c>IndexManyAsync</c>/<c>DeleteManyAsync</c> on
/// <c>ISearchIndex&lt;TDocument&gt;</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>A rate/pacing knob, not a new concurrency dimension:</b> both providers already dispatch a bulk
/// write as a sequence of batches internally (ElasticSearch's own document/byte-size chunking,
/// Meilisearch's own document-count chunking). Neither provider issues concurrent in-flight batch
/// requests today, and introducing parallel dispatch machinery is a materially larger, riskier change
/// than protecting the engine's availability for concurrent read/query traffic requires. This type caps
/// how fast that existing, already-sequential batch-dispatch loop may proceed — it does not introduce
/// batching where none exists, and it does not make dispatch concurrent.
/// </para>
/// <para>
/// <b>A genuinely neutral abstraction:</b> both providers can honor a batch-dispatch-rate cap
/// completely and identically, so this type lives here rather than in either provider's own options
/// type. It is complementary to, and independent of, each provider's own chunk-<em>size</em> knobs
/// (e.g. an ElasticSearch <c>BulkMaxBytes</c>/<c>BulkMaxDocuments</c> or a Meilisearch
/// <c>DefaultBatchSize</c>) — this is a dispatch-<em>rate</em> knob layered on top of whatever chunk
/// size those already produce, not a replacement for them.
/// </para>
/// <para>
/// <see cref="Default"/> (<see cref="MaxBatchesPerSecond"/> left <see langword="null"/>) is
/// byte-for-byte today's unthrottled, sequential behavior — the existing 3-argument
/// <c>IndexManyAsync</c>/<c>DeleteManyAsync</c> overloads delegate to their new 4-argument siblings
/// passing this instance.
/// </para>
/// </remarks>
public sealed record SearchBulkWriteOptions
{
    private readonly double? _maxBatchesPerSecond;

    /// <summary>
    /// Gets the maximum number of batch dispatches per second a bulk write may issue, or
    /// <see langword="null"/> for unthrottled — today's behavior, unchanged.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The assigned value is zero or negative. A rate cap of zero or less is not a valid pace; a
    /// programming error caught at first use, mirroring <c>TenantScope.Of</c> and
    /// <c>SearchFilter.Between</c>'s convention of throwing rather than returning a <c>Result</c> for
    /// an invalid literal supplied by the calling code itself.
    /// </exception>
    public double? MaxBatchesPerSecond
    {
        get => _maxBatchesPerSecond;
        init
        {
            if (value is <= 0)
            {
                throw new ArgumentException(
                    "MaxBatchesPerSecond must be a positive value when set.", nameof(value));
            }

            _maxBatchesPerSecond = value;
        }
    }

    /// <summary>Gets the default, unthrottled options instance.</summary>
    public static SearchBulkWriteOptions Default { get; } = new();
}
