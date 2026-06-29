namespace SharedKernel.Application.Behaviors.Idempotency;

/// <summary>
/// A minimal seam for recording and querying whether a given idempotency key has already been
/// processed.
/// </summary>
/// <remarks>
/// Mirrors the <c>HasProcessedAsync</c>/<c>MarkProcessedAsync</c> shape already proven by
/// <c>07.Messaging.Abstractions.IIdempotencyStore</c> — but this is this domain's own interface,
/// never a direct reference to <c>07.Messaging</c> (<c>05.Application</c>'s layering ceiling is
/// <c>01–04</c>). The consuming service provides the implementation (typically backed by the same
/// distributed store <c>07.Messaging</c>'s <c>IIdempotencyStore</c> uses, or a dedicated
/// table/cache key) and registers it at the composition root. This package ships only the
/// interface — no implementation, exactly like the <c>IUnitOfWork</c> precedent.
/// </remarks>
public interface IIdempotencyKeyStore
{
    /// <summary>Determines whether <paramref name="idempotencyKey"/> has already been processed.</summary>
    /// <param name="idempotencyKey">The idempotency key supplied by the command instance.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    Task<bool> HasProcessedAsync(string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>Records <paramref name="idempotencyKey"/> as successfully processed.</summary>
    /// <param name="idempotencyKey">The idempotency key supplied by the command instance.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    Task MarkProcessedAsync(string idempotencyKey, CancellationToken cancellationToken);
}
