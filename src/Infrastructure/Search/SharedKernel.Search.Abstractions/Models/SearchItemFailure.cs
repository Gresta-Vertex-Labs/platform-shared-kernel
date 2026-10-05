using SharedKernel.Primitives.Errors;

namespace SharedKernel.Search.Abstractions.Models;

/// <summary>A single document's failure within a bulk write, carried inside <see cref="SearchBulkReceipt"/>.</summary>
public sealed record SearchItemFailure
{
    /// <summary>Gets the id of the document that failed.</summary>
    public required string DocumentId { get; init; }

    /// <summary>Gets the structured error describing why this document failed.</summary>
    public required Error Error { get; init; }
}
