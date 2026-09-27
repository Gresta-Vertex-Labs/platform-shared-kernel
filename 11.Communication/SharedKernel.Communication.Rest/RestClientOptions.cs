using System.ComponentModel.DataAnnotations;

namespace SharedKernel.Communication;

/// <summary>
/// One REST client: its address, timeouts, retries, circuit breaker and credentials. Bound from
/// <c>SharedKernel:Communication:Clients:{name}</c> and validated at startup; change it in code with
/// <c>client.Configure(o =&gt; …)</c>.
/// </summary>
/// <remarks>
/// The defaults are Microsoft.Extensions.Http.Resilience's standard pipeline: 3 retries with exponential backoff and
/// jitter, 10 s per attempt, 30 s in total, and a circuit breaker that opens for 5 s when 10% of at least 100 calls in
/// 30 s fail. Only idempotent methods (GET, HEAD, OPTIONS, PUT, DELETE) are retried — a POST or PATCH is retried
/// only when it carries an <c>Idempotency-Key</c> (<see cref="PropagateIdempotencyKey"/>).
/// </remarks>
/// <example>
/// <code>
/// "Clients": {
///   "inventory": {
///     "BaseAddress": "http://inventory",
///     "AttemptTimeout": "00:00:05",
///     "PropagateIdempotencyKey": true,
///     "Authentication": { "Mode": "ApiKey", "ApiKey": { "Value": "from a secret store" } }
///   }
/// }
/// </code>
/// </example>
public sealed class RestClientOptions : CommunicationClientOptions
{
    /// <summary>
    /// Gets or sets the service's address, such as <c>http://inventory</c> (resolved through service discovery),
    /// <c>https+http://inventory</c> or <c>https://api.example.com/v2/</c>. Required. A relative request path is
    /// appended to it, so end a base path with <c>/</c>.
    /// </summary>
    public Uri? BaseAddress { get; set; }

    /// <summary>Gets or sets how long one attempt may take. Defaults to 10 seconds.</summary>
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Gets or sets how long the whole call may take, retries and their delays included. Defaults to 30 seconds.</summary>
    public TimeSpan TotalTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Gets or sets the retry settings.</summary>
    public RestRetryOptions Retry { get; set; } = new();

    /// <summary>Gets or sets the circuit breaker settings.</summary>
    public RestCircuitBreakerOptions CircuitBreaker { get; set; } = new();

    /// <summary>Gets or sets the hedging settings, used by a client registered with <c>UseHedging()</c>.</summary>
    public RestHedgingOptions Hedging { get; set; } = new();

    /// <summary>
    /// Gets or sets whether every POST and PATCH carries an <c>Idempotency-Key</c> header — a new key per call, the
    /// same on each of its retries — so the called service can recognise a repeat (<c>14.Presentation</c>'s
    /// <c>[RequireIdempotencyKey]</c>). Turning it on also lets those methods be retried. A key the caller set on the
    /// request is kept. Defaults to <see langword="false"/>.
    /// </summary>
    public bool PropagateIdempotencyKey { get; set; }

    /// <summary>Gets whether POST and PATCH may be retried or hedged.</summary>
    internal bool RetriesNonIdempotentMethods => PropagateIdempotencyKey || Retry.RetryNonIdempotentMethods;

    /// <inheritdoc />
    public override IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ValidateAddress(BaseAddress, nameof(BaseAddress)) is { } address)
        {
            yield return address;
        }

        if (AttemptTimeout <= TimeSpan.Zero || AttemptTimeout > TimeSpan.FromMinutes(10))
        {
            yield return new ValidationResult("AttemptTimeout must be positive and at most 10 minutes.", [nameof(AttemptTimeout)]);
        }

        if (TotalTimeout < AttemptTimeout || TotalTimeout > TimeSpan.FromHours(1))
        {
            yield return new ValidationResult("TotalTimeout must be at least AttemptTimeout and at most 1 hour.", [nameof(TotalTimeout)]);
        }

        if (Retry.MaxRetryAttempts is < 0 or > 10)
        {
            yield return new ValidationResult("Retry:MaxRetryAttempts must be between 0 and 10.", [nameof(Retry)]);
        }

        if (Retry.BaseDelay < TimeSpan.Zero || Retry.BaseDelay > TimeSpan.FromMinutes(1))
        {
            yield return new ValidationResult("Retry:BaseDelay must be between zero and 1 minute.", [nameof(Retry)]);
        }

        if (CircuitBreaker.Enabled)
        {
            if (CircuitBreaker.FailureRatio is <= 0 or > 1)
            {
                yield return new ValidationResult("CircuitBreaker:FailureRatio must be greater than 0 and at most 1.", [nameof(CircuitBreaker)]);
            }

            if (CircuitBreaker.MinimumThroughput < 2)
            {
                yield return new ValidationResult("CircuitBreaker:MinimumThroughput must be at least 2.", [nameof(CircuitBreaker)]);
            }

            if (CircuitBreaker.SamplingDuration < AttemptTimeout * 2)
            {
                yield return new ValidationResult(
                    $"CircuitBreaker:SamplingDuration ({CircuitBreaker.SamplingDuration}) must be at least twice AttemptTimeout ({AttemptTimeout}).",
                    [nameof(CircuitBreaker)]);
            }

            if (CircuitBreaker.BreakDuration < TimeSpan.FromMilliseconds(500) || CircuitBreaker.BreakDuration > TimeSpan.FromDays(1))
            {
                yield return new ValidationResult("CircuitBreaker:BreakDuration must be between 0.5 seconds and 1 day.", [nameof(CircuitBreaker)]);
            }
        }

        if (Hedging.MaxHedgedAttempts is < 1 or > 10 || Hedging.Delay < TimeSpan.Zero)
        {
            yield return new ValidationResult("Hedging:MaxHedgedAttempts must be between 1 and 10 and Hedging:Delay not negative.", [nameof(Hedging)]);
        }

        foreach (var result in base.Validate(validationContext))
        {
            yield return result;
        }
    }
}

/// <summary>How a failed attempt is retried: on a timeout, a lost connection, 408, 429 or a 5xx.</summary>
public sealed class RestRetryOptions
{
    /// <summary>Gets or sets the retries after the first attempt; <c>0</c> turns retries off. Defaults to 3.</summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Gets or sets the delay before the first retry; each next one doubles, with jitter. A <c>Retry-After</c> the
    /// service sends is honoured instead. Defaults to 500 ms.
    /// </summary>
    public TimeSpan BaseDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Gets or sets whether POST and PATCH are retried without an <c>Idempotency-Key</c>. Leave it off unless the
    /// service is known to treat them idempotently: a retry after a lost response repeats the side effect. Defaults to
    /// <see langword="false"/>.
    /// </summary>
    public bool RetryNonIdempotentMethods { get; set; }
}

/// <summary>
/// The circuit breaker: when too many calls fail, calls are refused for a while without being sent
/// (<c>communication.circuit_open</c>), so a struggling service gets room to recover.
/// </summary>
public sealed class RestCircuitBreakerOptions
{
    /// <summary>Gets or sets whether the breaker is on. Defaults to <see langword="true"/>.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets the share of failed calls in <see cref="SamplingDuration"/> that opens it. Defaults to 0.1.</summary>
    public double FailureRatio { get; set; } = 0.1;

    /// <summary>Gets or sets how many calls <see cref="SamplingDuration"/> must see before it may open. Defaults to 100.</summary>
    public int MinimumThroughput { get; set; } = 100;

    /// <summary>Gets or sets the window calls are counted over; at least twice the attempt timeout. Defaults to 30 seconds.</summary>
    public TimeSpan SamplingDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Gets or sets how long it stays open before a trial call is let through. Defaults to 5 seconds.</summary>
    public TimeSpan BreakDuration { get; set; } = TimeSpan.FromSeconds(5);
}

/// <summary>
/// Hedging, for a client registered with <c>UseHedging()</c>: when an attempt is slow, a parallel one goes to the next
/// endpoint, and the first answer wins — lower tail latency for reads, at the cost of extra load. POST and PATCH are
/// never hedged unless they carry an <c>Idempotency-Key</c>.
/// </summary>
public sealed class RestHedgingOptions
{
    /// <summary>Gets or sets the extra attempts that may run beside the first. Defaults to 1.</summary>
    public int MaxHedgedAttempts { get; set; } = 1;

    /// <summary>Gets or sets how long an attempt may run before the next one starts. Defaults to 2 seconds.</summary>
    public TimeSpan Delay { get; set; } = TimeSpan.FromSeconds(2);
}
