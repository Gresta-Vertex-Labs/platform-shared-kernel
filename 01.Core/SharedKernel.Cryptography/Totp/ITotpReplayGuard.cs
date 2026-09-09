namespace SharedKernel.Cryptography.Totp;

/// <summary>
/// Tracks previously-accepted TOTP codes so the same code cannot be accepted twice inside its
/// validity window. Implemented by the consuming service — this package ships no default
/// implementation.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors <c>12.Security.Oidc</c>'s DPoP replay-check seam: a pluggable contract, never a direct
/// <c>02.Caching</c> reference from this package. A typical implementation is backed by a
/// distributed cache/store keyed by <c>identityKey</c> (e.g. the user id or TOTP enrollment id)
/// with an entry expiring after <c>validityWindow</c> so the store never grows unbounded.
/// </para>
/// <para>
/// <b>BREAKING (as of P-514/WO-083): this interface's shape changed.</b> The prior two-step
/// <c>HasBeenUsedAsync</c> (check) + <c>MarkUsedAsync</c> (mark) pair has been REMOVED and
/// replaced by a single atomic member, <see cref="TryMarkUsedAsync"/>. The two-step shape was a
/// textbook TOCTOU: two concurrent <see cref="TotpVerifier.VerifyAsync"/> calls presenting the
/// same valid code could both observe "not yet used" via <c>HasBeenUsedAsync</c> before either
/// one called <c>MarkUsedAsync</c>, so both would be accepted. <b>MIGRATION:</b> an existing
/// implementer of the removed <c>HasBeenUsedAsync</c>/<c>MarkUsedAsync</c> pair must collapse both
/// steps into one atomic <see cref="TryMarkUsedAsync"/> — e.g. a single
/// <c>ConcurrentDictionary&lt;string, byte&gt;.TryAdd</c> call, a store's native atomic
/// reservation primitive (Redis <c>SET NX PX</c>, a SQL <c>INSERT ... ON CONFLICT DO NOTHING</c>),
/// or an explicit lock guarding a check-then-set sequence. Re-implementing this member by
/// internally calling a separate check followed by a separate set reintroduces the exact TOCTOU
/// this change exists to close and must never be done, even by a future implementer composing
/// this interface's single member by hand.
/// </para>
/// </remarks>
public interface ITotpReplayGuard
{
    /// <summary>
    /// Atomically checks whether <paramref name="code"/> has already been marked used for
    /// <paramref name="identityKey"/> and, if not, marks it used for <paramref name="validityWindow"/>
    /// — in one indivisible operation.
    /// </summary>
    /// <param name="identityKey">The identity the code was presented for (e.g. a user id or enrollment id).</param>
    /// <param name="code">The candidate code, already confirmed cryptographically valid by the caller.</param>
    /// <param name="validityWindow">
    /// How long this code should continue to be reported as used (i.e. reserved) once this call
    /// marks it used.
    /// </param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>
    /// <see langword="true"/> when THIS call is the first to mark <c>(identityKey, code)</c> used
    /// — a fresh code, now consumed. <see langword="false"/> when it was already marked used by an
    /// earlier call — a replay.
    /// </returns>
    /// <remarks>
    /// Implementations MUST perform the check-and-mark as one atomic operation (e.g. a single
    /// atomic dictionary insert, a distributed store's native conditional-write primitive, or an
    /// operation guarded by a lock) — never a separate read followed by a separate write, which
    /// reopens the TOCTOU window this member exists to close.
    /// </remarks>
    ValueTask<bool> TryMarkUsedAsync(string identityKey, string code, TimeSpan validityWindow, CancellationToken ct = default);
}
