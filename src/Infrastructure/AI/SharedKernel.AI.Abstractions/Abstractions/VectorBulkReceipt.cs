namespace SharedKernel.AI.Abstractions.Abstractions;

/// <summary>
/// The acknowledgement of a bulk write (or delete) operation against a vector collection.
/// </summary>
/// <remarks>
/// <b>Bulk partial failure is not collapsed:</b> <c>UpsertManyAsync</c>/<c>DeleteManyAsync</c> return a
/// successful <see cref="SharedKernel.Primitives.Results.Result{T}"/> carrying a
/// <see cref="VectorBulkReceipt"/> even when <see cref="Failures"/> is non-empty — both engines' batch
/// APIs report per-item failures within an otherwise-successful batch call.
/// <see cref="SharedKernel.Primitives.Results.Result{T}.Failure(SharedKernel.Primitives.Errors.Error)"/>
/// is reserved for "the request itself did not execute" (e.g. a model-identity/dimension mismatch
/// caught before any I/O).
/// </remarks>
public sealed record VectorBulkReceipt
{
    /// <summary>Gets the overall write receipt for this bulk operation.</summary>
    public required VectorWriteReceipt Receipt { get; init; }

    /// <summary>Gets the number of records that succeeded.</summary>
    public required int SucceededCount { get; init; }

    /// <summary>Gets the per-record failures within this bulk operation.</summary>
    public required IReadOnlyList<VectorItemFailure> Failures { get; init; }

    /// <summary>Gets a value indicating whether any record in this bulk operation failed.</summary>
    public bool HasFailures => Failures.Count > 0;
}
