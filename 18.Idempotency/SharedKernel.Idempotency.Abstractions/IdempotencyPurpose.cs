namespace SharedKernel.Idempotency.Abstractions;

/// <summary>
/// What an idempotency key guards. Stores are registered keyed by purpose, so a service can back
/// requests and messages with different stores, and a store keeps the two key spaces apart.
/// </summary>
public enum IdempotencyPurpose
{
    /// <summary>
    /// A command sent through the application pipeline, guarded by a caller-supplied idempotency key.
    /// The fingerprint identifies the request body, and a completed reservation stores the response to
    /// replay.
    /// </summary>
    Request,

    /// <summary>
    /// A message delivered to a consumer, guarded by its message id. A completed reservation stores no
    /// response: a duplicate delivery is simply acknowledged.
    /// </summary>
    Message,
}
