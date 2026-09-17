namespace SharedKernel.Caching.Abstractions;

/// <summary>
/// A held distributed lock. Kept alive by the implementation until disposed.
/// </summary>
/// <remarks>
/// <para>
/// Dispose the handle (<c>await using</c>) to release the lock; releasing is idempotent.
/// </para>
/// <para>
/// If keeping the lock alive fails, for example because the store became unreachable,
/// <see cref="IsHeld"/> turns <see langword="false"/> and <see cref="LostToken"/> is cancelled. Pass
/// <see cref="LostToken"/> (linked with your own token) to long-running work so it stops when the
/// lock is lost.
/// </para>
/// </remarks>
/// <seealso cref="IDistributedLockService.TryAcquireAsync"/>
public interface IDistributedLock : IAsyncDisposable
{
    /// <summary>Gets the locked resource name.</summary>
    string Resource { get; }

    /// <summary>
    /// Gets a token that is strictly greater than the token of every earlier acquisition of the same
    /// resource. Gaps are normal.
    /// </summary>
    long FencingToken { get; }

    /// <summary>Gets a value indicating whether the lock is still held.</summary>
    bool IsHeld { get; }

    /// <summary>Gets a token that is cancelled when the lock is lost or released.</summary>
    CancellationToken LostToken { get; }
}
