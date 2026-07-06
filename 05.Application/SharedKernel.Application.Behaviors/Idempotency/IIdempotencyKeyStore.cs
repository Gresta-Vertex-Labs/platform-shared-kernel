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
/// <remarks>
/// <b>Optional response replay (WO-039, P-242):</b> an implementation MAY additionally implement
/// <see cref="IIdempotencyResponseStore"/> to opt in to replaying the original response on a
/// duplicate submission instead of always returning <c>Error.Conflict</c>. This is purely additive —
/// an implementation of only this interface continues to compile and behave exactly as before that
/// capability existed. <see cref="IdempotentCommandBehavior{TRequest,TResponse}"/> always calls
/// <see cref="HasProcessedAsync"/>/<see cref="MarkProcessedAsync"/> first, regardless of whether
/// replay is supported.
/// </remarks>
public interface IIdempotencyKeyStore
{
    /// <summary>Determines whether <paramref name="idempotencyKey"/> has already been processed.</summary>
    /// <param name="idempotencyKey">The idempotency key supplied by the command instance.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    Task<bool> HasProcessedAsync(string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>Records <paramref name="idempotencyKey"/> as processed.</summary>
    /// <param name="idempotencyKey">The idempotency key supplied by the command instance.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <remarks>
    /// <para>
    /// <b>Fail-and-consume-key invariant (documented, not an oversight):</b>
    /// <see cref="IdempotentCommandBehavior{TRequest,TResponse}"/> calls this method after
    /// <c>next()</c> returns — including when the handler returns a <c>Result.Failure</c>
    /// (a business-rule failure, not a thrown exception). This means a failed command
    /// <b>permanently consumes its idempotency key</b>.
    /// </para>
    /// <para>
    /// <b>Client retry rule:</b> after a business-rule failure the client <b>must</b> use a
    /// <b>new</b> idempotency key if it wishes to retry the command. Retrying with the same
    /// key after a business-rule rejection would bypass the duplicate-submission guard rather
    /// than correct the underlying business condition.
    /// </para>
    /// <para>
    /// <b>Fault vs. failure asymmetry:</b>
    /// <list type="bullet">
    ///   <item>
    ///     <term>Fault (thrown exception)</term>
    ///     <description>Key is <b>not</b> consumed — <see cref="MarkProcessedAsync"/> is only
    ///     reached when <c>next()</c> returns normally; a thrown exception bypasses this call
    ///     entirely, so retry with the same key is safe.</description>
    ///   </item>
    ///   <item>
    ///     <term>Failure (<c>Result.Failure</c>)</term>
    ///     <description>Key <b>is</b> consumed — the handler was reached, evaluated the command,
    ///     and returned a deliberate business-rule rejection; a new idempotency key is required
    ///     to retry.</description>
    ///   </item>
    /// </list>
    /// </para>
    /// </remarks>
    Task MarkProcessedAsync(string idempotencyKey, CancellationToken cancellationToken);
}
