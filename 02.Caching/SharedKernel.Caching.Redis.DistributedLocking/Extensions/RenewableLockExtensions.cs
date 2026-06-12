using SharedKernel.Caching.Abstractions;

namespace SharedKernel.Caching.Redis.DistributedLocking.Extensions;

/// <summary>
/// Extension methods for <see cref="IRenewableLock"/>.
/// </summary>
public static class RenewableLockExtensions
{
    /// <summary>
    /// Continuously renews <paramref name="lock"/> on the specified
    /// <paramref name="renewalInterval"/> until the lock is lost or
    /// <paramref name="ct"/> is cancelled.
    /// </summary>
    /// <param name="lock">
    /// The renewable lock to keep alive. Must not be <see langword="null"/>.
    /// The caller owns this lock and is responsible for disposing it —
    /// <c>KeepAliveAsync</c> does not call <see cref="IAsyncDisposable.DisposeAsync"/>.
    /// </param>
    /// <param name="renewalInterval">
    /// How long to wait between renewal attempts. Should be shorter than the lock's
    /// <c>expiry</c> parameter to ensure renewal reaches Redis before expiry.
    /// A value of roughly 50–70% of <c>expiry</c> is recommended.
    /// </param>
    /// <param name="ct">
    /// Cancellation token. The caller is responsible for cancelling this token to
    /// stop the keep-alive loop; <c>KeepAliveAsync</c> does not own the token.
    /// When cancelled, the method completes without throwing
    /// <see cref="OperationCanceledException"/>.
    /// </param>
    /// <returns>
    /// A <see cref="Task"/> that completes when the loop exits — either because
    /// <see cref="IRenewableLock.RenewAsync"/> returned <see langword="false"/>
    /// (lock was lost) or <paramref name="ct"/> was cancelled.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The caller is responsible for cancellation and for disposing the lock. Example:
    /// <code>
    /// using var cts = CancellationTokenSource.CreateLinkedTokenSource(operationCt);
    ///
    /// await using var renewableLock = await lockService.AcquireRenewableAsync(
    ///     "resource:42", expiry, wait, retry, operationCt);
    ///
    /// if (renewableLock is null) return;
    ///
    /// // Keep the lock alive in the background.
    /// var keepAlive = renewableLock.KeepAliveAsync(
    ///     renewalInterval: TimeSpan.FromSeconds(20),
    ///     ct: cts.Token);
    ///
    /// try
    /// {
    ///     await DoLongRunningWorkAsync(operationCt);
    /// }
    /// finally
    /// {
    ///     await cts.CancelAsync();
    ///     await keepAlive; // wait for the loop to exit cleanly
    /// }
    /// </code>
    /// </para>
    /// </remarks>
    public static async Task KeepAliveAsync(
        this IRenewableLock @lock,
        TimeSpan renewalInterval,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(@lock);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(renewalInterval, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Cancellation is the normal exit path — return cleanly.
                return;
            }

            // Exit if lock is already gone (dispose was called externally).
            if (!@lock.IsAcquired)
                return;

            bool renewed;
            try
            {
                renewed = await @lock.RenewAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (!renewed)
                return;
        }
    }
}
