namespace SharedKernel.Application.Behaviors.Idempotency;

/// <summary>
/// The <see cref="SharedKernel.Primitives.Errors.Error.Code"/> values
/// <see cref="IdempotencyBehavior{TRequest,TResponse}"/> fails with.
/// </summary>
/// <remarks>
/// Clients and dashboards branch on these values, so they never change. <see cref="KeyRequired"/> is also the code
/// <c>14.Presentation</c> answers a request without an <c>Idempotency-Key</c> header with; a <c>00.Governance</c> test
/// keeps the two equal.
/// </remarks>
public static class IdempotencyErrorCodes
{
    /// <summary>
    /// <c>idempotency.key_required</c>: the command's idempotency key is empty or whitespace (a validation failure).
    /// </summary>
    public const string KeyRequired = "idempotency.key_required";

    /// <summary>
    /// <c>idempotency.in_progress</c>: another request with the same key and caller is still running (a conflict).
    /// Retry later; the retry replays that request's response once it completes.
    /// </summary>
    public const string InProgress = "idempotency.in_progress";

    /// <summary>
    /// <c>idempotency.key_reused</c>: the key was already used by the same caller for a request with a different
    /// fingerprint (a conflict). Send a new key for a new request.
    /// </summary>
    public const string KeyReused = "idempotency.key_reused";
}
