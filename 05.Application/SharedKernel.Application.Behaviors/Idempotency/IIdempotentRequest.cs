namespace SharedKernel.Application.Behaviors.Idempotency;

/// <summary>
/// Marks a command as opting in to duplicate-submission protection (double-click, client retry).
/// </summary>
/// <remarks>
/// Never implemented by a query — queries are already idempotent by definition (a query that
/// needs caching uses <c>ICacheableQuery&lt;TResponse&gt;</c> instead, an orthogonal concern). The
/// key is supplied by the command instance itself (e.g. a client-generated idempotency token, or a
/// deterministic hash of the command's discriminating fields) — this package never generates keys.
/// </remarks>
public interface IIdempotentRequest
{
    /// <summary>Gets the idempotency key for this command instance.</summary>
    string IdempotencyKey { get; }
}
