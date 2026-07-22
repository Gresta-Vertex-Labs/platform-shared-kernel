namespace SharedKernel.AI.Abstractions.Abstractions;

/// <summary>One chunk of a streaming completion call.</summary>
public sealed record CompletionChunk
{
    /// <summary>Gets the incremental content delta carried by this chunk. Never log this value.</summary>
    public required string DeltaContent { get; init; }

    /// <summary>
    /// Gets why generation stopped, or <see langword="null"/> until the final chunk.
    /// </summary>
    public CompletionFinishReason? FinishReason { get; init; }

    /// <summary>
    /// Gets the token usage this call billed, or <see langword="null"/> until the final chunk — most
    /// providers report usage only once the stream completes.
    /// </summary>
    public TokenUsage? TokenUsage { get; init; }
}
