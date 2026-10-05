namespace SharedKernel.AI.SemanticKernel.Resilience;

/// <summary>
/// Explicit, bounded, opt-in retry configuration for <see cref="Orchestration.SemanticKernelOrchestrator.CompleteAsync"/> —
/// never applied unless the composition root calls <c>.WithBoundedRetry(...)</c>.
/// </summary>
/// <remarks>
/// Per Domain Invariant #5, a retry re-bills and re-rolls a non-deterministic output — this type exists
/// solely to make that trade-off explicit and bounded when a caller genuinely opts in, never automatic.
/// It is never applied to <c>CompleteStreamingAsync</c> — retrying a partially-streamed response would
/// duplicate already-yielded content.
/// </remarks>
internal sealed class BoundedRetryOptions
{
    public BoundedRetryOptions(int maxAttempts, TimeSpan baseDelay)
    {
        if (maxAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), maxAttempts, "MaxAttempts must be at least 1.");
        }

        MaxAttempts = maxAttempts;
        BaseDelay = baseDelay;
    }

    /// <summary>Gets the maximum number of attempts (including the first), never automatic and always bounded.</summary>
    public int MaxAttempts { get; }

    /// <summary>Gets the base delay used for exponential backoff between attempts.</summary>
    public TimeSpan BaseDelay { get; }

    /// <summary>Computes the delay before the given (1-based) retry attempt using exponential backoff.</summary>
    public TimeSpan ComputeDelay(int attempt) => BaseDelay * Math.Pow(2, Math.Max(attempt - 1, 0));
}
