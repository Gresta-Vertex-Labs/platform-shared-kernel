namespace SharedKernel.Search.Abstractions.Models;

/// <summary>
/// The acknowledgement returned by a bulk write — <c>IndexManyAsync</c>/<c>DeleteManyAsync</c> on
/// <c>ISearchIndex&lt;TDocument&gt;</c>.
/// </summary>
/// <remarks>
/// Partial failure is normal operation in an ElasticSearch <c>_bulk</c> response; the per-item errors
/// in <see cref="Failures"/> are the only actionable output. The outer <c>Result</c> stays
/// <b>success</b> even when <see cref="Failures"/> is non-empty — <c>Result.Failure</c> is reserved
/// for "the request itself did not execute". Callers requiring all-or-nothing semantics check
/// <see cref="HasFailures"/>.
/// </remarks>
public sealed record SearchBulkReceipt
{
    /// <summary>Gets the write receipt for the bulk operation as a whole.</summary>
    public required SearchWriteReceipt Receipt { get; init; }

    /// <summary>Gets the number of documents that succeeded.</summary>
    public required int SucceededCount { get; init; }

    /// <summary>Gets the per-document failures, one <see cref="SearchItemFailure"/> per failed document.</summary>
    public IReadOnlyList<SearchItemFailure> Failures { get; init; } = [];

    /// <summary>Gets a value indicating whether any document in this bulk operation failed.</summary>
    public bool HasFailures => Failures.Count > 0;
}
