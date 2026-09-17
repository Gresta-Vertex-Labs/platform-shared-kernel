namespace SharedKernel.Application.Behaviors.Idempotency;

/// <summary>
/// Marks a command as opting in to duplicate-submission protection (double-click, client retry).
/// </summary>
/// <remarks>
/// Never implemented by a query — queries are already idempotent by definition (a query that
/// needs caching uses <c>ICacheableQuery&lt;TValue&gt;</c> instead, an orthogonal concern). The
/// key is supplied by the command instance itself (e.g. a client-generated idempotency token, or a
/// deterministic hash of the command's discriminating fields) — this package never generates keys.
/// </remarks>
public interface IIdempotentRequest
{
    /// <summary>Gets the idempotency key for this command instance.</summary>
    string IdempotencyKey { get; }

    /// <summary>
    /// Gets an optional, caller-supplied fingerprint distinguishing a genuine retry of this exact
    /// command from a different command that happens to reuse the same <see cref="IdempotencyKey"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When <see langword="null"/> or whitespace-only (the default), <c>IdempotencyBehavior</c>
    /// falls back to a SHA-256 hash of the command's default JSON serialization — fine for most
    /// commands, but with two sharp edges a command should override this to avoid:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>The automatic hash changes whenever the command type gains, loses, or reorders a
    ///   serialized property — a client retrying the exact same logical request across a deploy that
    ///   shipped such a change is rejected as <c>idempotency.key_reused</c>, not replayed.</description></item>
    ///   <item><description>A command carrying a client-supplied timestamp or a freshly generated identifier
    ///   (e.g. a new correlation id per attempt) never serializes identically twice, so it can never match
    ///   itself on retry — every retry looks like a different request reusing the same key.</description></item>
    /// </list>
    /// <para>
    /// A command exposed to either hazard should return a stable fingerprint computed only from its
    /// own discriminating business fields (e.g. customer id + amount), excluding any field that
    /// legitimately varies between otherwise-identical retries.
    /// </para>
    /// </remarks>
    string? Fingerprint => null;
}
