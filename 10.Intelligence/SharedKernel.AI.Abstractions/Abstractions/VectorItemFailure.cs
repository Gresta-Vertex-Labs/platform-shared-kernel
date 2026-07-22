using SharedKernel.Primitives.Errors;

namespace SharedKernel.AI.Abstractions.Abstractions;

/// <summary>One failed item within an otherwise-successful bulk write.</summary>
public sealed record VectorItemFailure
{
    /// <summary>Gets the id of the record that failed.</summary>
    public required string RecordId { get; init; }

    /// <summary>Gets the structured error describing why the item failed.</summary>
    public required Error Error { get; init; }
}
