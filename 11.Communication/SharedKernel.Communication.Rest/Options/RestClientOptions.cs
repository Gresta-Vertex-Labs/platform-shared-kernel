using Microsoft.Extensions.Options;

namespace SharedKernel.Communication.Rest.Options;

/// <summary>
/// Configuration for a typed REST client registered via
/// <c>IRestCommunicationBuilder.AddRestClient&lt;TClient&gt;</c>.
/// </summary>
public sealed class RestClientOptions
{
    /// <summary>
    /// Base address for the typed client. Required when <c>IServiceEndpointResolver</c> is not
    /// registered in the DI container; omit to enable at-request-time service-discovery resolution.
    /// </summary>
    public string? BaseAddress { get; set; }

    /// <summary>
    /// Overrides the <c>name</c> parameter for DNS lookup via <c>IServiceEndpointResolver</c>.
    /// Use when the typed client's logical registration name differs from its DNS service name.
    /// When <c>null</c> (default), the <c>name</c> parameter passed to
    /// <c>AddRestClient&lt;TClient&gt;</c> is used for DNS resolution.
    /// </summary>
    public string? ServiceName { get; set; }

    /// <summary>
    /// Per-request timeout in seconds applied via <c>StandardResilienceHandler</c>.
    /// Default: 30 s.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Resilience pipeline settings (retry, circuit breaker).</summary>
    public RestResilienceOptions Resilience { get; set; } = new();

    /// <summary>
    /// When <c>true</c>, attaches a stable <c>Idempotency-Key</c> header to every outgoing request
    /// via the opt-in <c>IdempotencyKeyDelegatingHandler</c>, generated once per logical call and
    /// preserved unchanged across every Polly-driven retry. Default: <c>false</c>.
    /// Enable for typed clients issuing non-idempotent verbs (POST/PATCH/DELETE) that a downstream
    /// service can deduplicate by this header, converting <c>StandardResilienceHandler</c>'s default
    /// retry behavior from a silent duplicate-side-effect hazard into an explicit, documented guarantee.
    /// </summary>
    public bool EnableIdempotencyKeyPropagation { get; set; }
}

/// <summary>
/// Validates <see cref="RestClientOptions"/> (and its nested <see cref="RestResilienceOptions"/>) for
/// clients that do not use service discovery. BaseAddress is required unless the caller has registered
/// an IServiceEndpointResolver separately. The endpoint-resolver check cannot be done here (no DI access)
/// — it is enforced in <c>RestCommunicationBuilder.AddRestClient&lt;TClient&gt;</c> at registration time.
/// Invoked directly by <c>RestCommunicationBuilder.AddRestClient&lt;TClient&gt;</c> against the
/// just-constructed options instance (P-358/WO-056) — this type is also registered as
/// <see cref="IValidateOptions{TOptions}"/> for any future direct <see cref="IOptions{TOptions}"/>
/// consumer, but that registration alone is not the enforcement mechanism relied upon, since
/// <see cref="RestClientOptions"/> is never resolved via <c>IOptions&lt;RestClientOptions&gt;.Value</c>.
/// </summary>
internal sealed class RestClientOptionsValidator : IValidateOptions<RestClientOptions>
{
    public ValidateOptionsResult Validate(string? name, RestClientOptions options)
    {
        var failures = new List<string>();

        // BaseAddress emptiness validation only — resolver presence is checked in the builder.
        // An explicit empty string is suspicious; null means "use service discovery".
        if (options.BaseAddress is not null && string.IsNullOrWhiteSpace(options.BaseAddress))
        {
            failures.Add(
                "RestClientOptions.BaseAddress must not be an empty or whitespace string. " +
                "Set it to a valid URI or leave it null to use IServiceEndpointResolver.");
        }

        if (options.TimeoutSeconds <= 0)
        {
            failures.Add(
                $"RestClientOptions.TimeoutSeconds must be greater than zero. Got: {options.TimeoutSeconds}.");
        }

        var resilience = options.Resilience;

        if (resilience.RetryCount <= 0)
        {
            failures.Add(
                $"RestResilienceOptions.RetryCount must be greater than zero. Got: {resilience.RetryCount}.");
        }

        if (resilience.RetryBaseDelayMs < 0)
        {
            failures.Add(
                $"RestResilienceOptions.RetryBaseDelayMs must not be negative. Got: {resilience.RetryBaseDelayMs}.");
        }

        if (resilience.FailureThreshold <= 0)
        {
            failures.Add(
                $"RestResilienceOptions.FailureThreshold must be greater than zero. Got: {resilience.FailureThreshold}.");
        }

        if (resilience.SamplingDurationSec <= 0)
        {
            failures.Add(
                $"RestResilienceOptions.SamplingDurationSec must be greater than zero. Got: {resilience.SamplingDurationSec}.");
        }

        if (resilience.BreakDurationSec <= 0)
        {
            failures.Add(
                $"RestResilienceOptions.BreakDurationSec must be greater than zero. Got: {resilience.BreakDurationSec}.");
        }

        if (resilience.TotalTimeoutBufferSec < 0)
        {
            failures.Add(
                $"RestResilienceOptions.TotalTimeoutBufferSec must not be negative. Got: {resilience.TotalTimeoutBufferSec}.");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
