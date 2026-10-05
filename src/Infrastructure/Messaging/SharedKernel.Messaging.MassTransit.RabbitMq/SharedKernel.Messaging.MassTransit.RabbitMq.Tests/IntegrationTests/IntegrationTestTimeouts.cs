namespace SharedKernel.Messaging.MassTransit.RabbitMq.Tests.IntegrationTests;

/// <summary>
/// Computes consume-wait ceilings for the RabbitMQ Testcontainers-backed integration tests in this
/// folder (<see cref="RabbitMqIntegrationTests"/>, <see cref="DeadLetterIntegrationTests"/>,
/// <see cref="OrderedDeliveryIntegrationTests"/>).
/// </summary>
/// <remarks>
/// <para>
/// Every test in this folder waits on a <see cref="System.Threading.Tasks.TaskCompletionSource{TResult}"/>
/// that resolves the instant its expected message(s) arrive — the timeout each test passes to its
/// consumer's <c>WaitAsync</c> is a CEILING on how long a genuinely stuck test is allowed to run, not a
/// fixed sleep. A broker that delivers promptly still returns in milliseconds; only a broker that never
/// delivers burns the full ceiling. That means the ceiling can be sized generously with zero cost to the
/// common case — the only real trade-off is how long a truly broken test takes to fail.
/// </para>
/// <para>
/// <strong>P-501:</strong> <c>OrderedDeliveryIntegrationTests</c> burned its full fixed 20-second ceiling
/// on <c>ubuntu-latest</c> and never reached its ordering assertions — a starved wait budget, not an
/// ordering defect. CI runs this project inside the integration lane alongside 17 other
/// Testcontainers-backed projects, capped to 2 concurrent VSTest hosts on a 2-core runner (see
/// <c>eng/testsettings/integration.runsettings</c>), so the RabbitMQ container competes for CPU with the
/// test host and everything else in the lane — a budget tuned on a full-size dev machine has no margin
/// left there. The other two tests in this folder use the identical fixed-ceiling pattern and share the
/// same latent fragility even though they happened to pass on this run.
/// </para>
/// <para>
/// <see cref="IsCi"/> detects a CI provider via the conventional <c>CI</c> environment variable (set to
/// <c>"true"</c> by GitHub Actions and most other providers) and multiplies every local budget by
/// <see cref="CiMultiplier"/> — local runs keep a tight, fast-failing budget; CI gets real headroom.
/// </para>
/// </remarks>
internal static class IntegrationTestTimeouts
{
    /// <summary>
    /// Multiplier applied to every local budget when <see cref="IsCi"/> is true. 3x was chosen as a
    /// deliberately generous, round number for a 2-core shared runner rather than a value reverse-fit to
    /// one observed failure.
    /// </summary>
    private const int CiMultiplier = 3;

    /// <summary>True when running under a CI provider (GitHub Actions and most others set <c>CI=true</c>).</summary>
    internal static bool IsCi { get; } = IsTruthy(Environment.GetEnvironmentVariable("CI"));

    /// <summary>
    /// The one-time delay after starting the bus's hosted services, before the first publish, to let the
    /// connection establish and topology bind. CI gets <see cref="CiMultiplier"/>x the local value for the
    /// same contention reasons as <see cref="Fixed"/>/<see cref="ScaledByMessageCount"/> below.
    /// </summary>
    internal static TimeSpan BusConnectDelay { get; } =
        IsCi ? TimeSpan.FromMilliseconds(500 * CiMultiplier) : TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// A consume-wait ceiling for a fixed, small amount of broker round-trip work that does not scale
    /// with message count (a single publish/consume hop, or a publish→fault→fault-consumer hop).
    /// </summary>
    /// <param name="localSeconds">The local-machine budget, in seconds; CI receives <see cref="CiMultiplier"/>x that.</param>
    internal static TimeSpan Fixed(int localSeconds) =>
        TimeSpan.FromSeconds(IsCi ? localSeconds * CiMultiplier : localSeconds);

    /// <summary>
    /// A consume-wait ceiling that additionally scales with the number of messages a test publishes, so a
    /// test parameterized to publish more messages does not inherit a ceiling sized for fewer.
    /// </summary>
    /// <param name="messageCount">Total number of messages the test expects to observe consumed.</param>
    /// <param name="baseSeconds">Fixed local overhead (connection/topology/broker latency), in seconds.</param>
    /// <param name="secondsPerMessage">Additional local budget per message, in seconds.</param>
    internal static TimeSpan ScaledByMessageCount(int messageCount, int baseSeconds, double secondsPerMessage)
    {
        var localSeconds = baseSeconds + (secondsPerMessage * messageCount);
        return TimeSpan.FromSeconds(IsCi ? localSeconds * CiMultiplier : localSeconds);
    }

    private static bool IsTruthy(string? value) =>
        !string.IsNullOrWhiteSpace(value) && !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
}
