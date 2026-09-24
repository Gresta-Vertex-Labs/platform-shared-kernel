namespace SharedKernel.Application.Idempotency;

/// <summary>
/// The outcome of an <see cref="IRequestIdempotencyStore.TryBeginAsync"/> call.
/// </summary>
public enum IdempotencyBeginStatus
{
    /// <summary>The key was not previously reserved; a new reservation was created and the caller should proceed.</summary>
    Started,

    /// <summary>The key is reserved by an in-flight, not-yet-completed execution.</summary>
    InProgress,

    /// <summary>The key was already completed with the same request fingerprint; its stored response should be replayed.</summary>
    Completed,

    /// <summary>The key exists but was recorded (in-flight or completed) against a different request fingerprint.</summary>
    FingerprintMismatch,
}
