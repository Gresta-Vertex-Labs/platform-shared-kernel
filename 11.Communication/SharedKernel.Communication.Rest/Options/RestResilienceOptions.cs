namespace SharedKernel.Communication.Rest.Options;

/// <summary>
/// Resilience policy settings applied via Polly v8 <c>StandardResilienceHandler</c>.
/// All defaults are conservative and production-safe.
/// </summary>
public sealed class RestResilienceOptions
{
    /// <summary>Number of retry attempts after the initial failure. Default: 3.</summary>
    public int RetryCount { get; set; } = 3;

    /// <summary>Base delay in milliseconds for exponential backoff between retries. Default: 500 ms.</summary>
    public int RetryBaseDelayMs { get; set; } = 500;

    /// <summary>Whether to enable the circuit breaker layer. Default: true.</summary>
    public bool CircuitBreakerEnabled { get; set; } = true;

    /// <summary>Number of failures within <see cref="SamplingDurationSec"/> that trip the circuit breaker. Default: 5.</summary>
    public int FailureThreshold { get; set; } = 5;

    /// <summary>Sliding window in seconds over which failures are counted. Default: 30 s.</summary>
    public int SamplingDurationSec { get; set; } = 30;

    /// <summary>Duration in seconds the circuit breaker stays open before allowing a probe request. Default: 30 s.</summary>
    public int BreakDurationSec { get; set; } = 30;
}
