namespace SharedKernel.Scheduling.Tests.TestSupport;

/// <summary>
/// Polls a condition against a REAL background service's REAL progress until it becomes true or a
/// timeout elapses.
/// </summary>
/// <remarks>
/// This is not a substitute for <see cref="SharedKernel.Testing.Clocks.FakeClock"/> — every test using
/// this helper still drives the DOMAIN's notion of time exclusively through <c>FakeClock</c>. This
/// helper only waits for <c>SchedulingHostedService</c>'s genuinely-asynchronous <c>PeriodicTimer</c>
/// loop (which runs on real wall-clock ticks by construction — see that type's own remarks) to have
/// actually observed and acted on a state change the test already made deterministic via
/// <c>FakeClock</c>.
/// </remarks>
internal static class Eventually
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(10);

    /// <summary>Polls <paramref name="condition"/> until it returns <see langword="true"/> or the timeout elapses.</summary>
    /// <returns><see langword="true"/> if the condition became true within the timeout; otherwise <see langword="false"/>.</returns>
    public static async Task<bool> UntilAsync(Func<bool> condition, TimeSpan? timeout = null, TimeSpan? pollInterval = null)
    {
        ArgumentNullException.ThrowIfNull(condition);

        var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);
        var interval = pollInterval ?? DefaultPollInterval;

        while (true)
        {
            if (condition())
            {
                return true;
            }

            if (DateTime.UtcNow >= deadline)
            {
                return condition();
            }

            await Task.Delay(interval).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Polls until <paramref name="condition"/> holds continuously for <paramref name="stableFor"/>,
    /// used to assert "nothing more happens" (e.g. an invocation count stays put) rather than "something
    /// eventually happens".
    /// </summary>
    public static async Task<bool> StaysAsync(Func<bool> condition, TimeSpan stableFor, TimeSpan? pollInterval = null)
    {
        ArgumentNullException.ThrowIfNull(condition);

        var deadline = DateTime.UtcNow + stableFor;
        var interval = pollInterval ?? DefaultPollInterval;

        while (DateTime.UtcNow < deadline)
        {
            if (!condition())
            {
                return false;
            }

            await Task.Delay(interval).ConfigureAwait(false);
        }

        return condition();
    }
}
