using System.ComponentModel.DataAnnotations;

namespace SharedKernel.Integration.Notifications.Abstractions.Options;

/// <summary>
/// Configures retry, backoff, timeout, and send-concurrency behavior for outbound notification
/// delivery.
/// </summary>
/// <remarks>
/// <para>
/// Bound to configuration section <c>"SharedKernel:Integration:Notifications"</c> and registered via
/// <c>AddSharedKernelNotifications</c> (<c>.AddOptions&lt;NotificationDeliveryOptions&gt;().BindConfiguration(...)
/// .ValidateDataAnnotations().ValidateOnStart()</c>), the exact mechanism
/// <c>WebhookDeliveryOptions</c> already uses. Mechanical bounds (<see cref="MaxAttempts"/> &gt;= 1,
/// <see cref="MaxConcurrentSends"/> &gt;= 1) are plain <see cref="RangeAttribute"/> annotations; the
/// cross-field rule (<see cref="MaxBackoffDelay"/> &gt;= <see cref="BaseBackoffDelay"/>) and the
/// positive-<see cref="TimeSpan"/> checks DataAnnotations attributes cannot express on their own are
/// implemented via <see cref="IValidatableObject.Validate"/>.
/// </para>
/// <para>
/// SHARED across every provider package — each provider's own named <see cref="System.Net.Http.HttpClient"/>
/// resilience wiring is configured from this one options type, never a per-provider duplicate
/// options shape for the retry/backoff/timeout/concurrency knobs. A provider-specific credential
/// (e.g. an API key) lives in that provider's own package, never here.
/// </para>
/// </remarks>
public sealed class NotificationDeliveryOptions : IValidatableObject
{
    /// <summary>The maximum number of send attempts made per notification. Defaults to 3.</summary>
    [Range(1, int.MaxValue)]
    public int MaxAttempts { get; set; } = 3;

    /// <summary>The initial delay before the first retry. Defaults to 1 second.</summary>
    public TimeSpan BaseBackoffDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>The maximum delay between retries. Defaults to 30 seconds.</summary>
    public TimeSpan MaxBackoffDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>The per-attempt HTTP request timeout. Defaults to 10 seconds.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The maximum number of concurrent sends permitted through a provider's named
    /// <see cref="System.Net.Http.HttpClient"/> at once, enforced via the resilience pipeline's rate limiter stage
    /// (<c>HttpStandardResilienceOptions.RateLimiter</c>) — no bespoke per-provider throttle type.
    /// Defaults to 16.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int MaxConcurrentSends { get; set; } = 16;

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (BaseBackoffDelay <= TimeSpan.Zero)
        {
            yield return new ValidationResult(
                $"{nameof(BaseBackoffDelay)} must be greater than zero.",
                [nameof(BaseBackoffDelay)]);
        }

        if (MaxBackoffDelay <= TimeSpan.Zero)
        {
            yield return new ValidationResult(
                $"{nameof(MaxBackoffDelay)} must be greater than zero.",
                [nameof(MaxBackoffDelay)]);
        }

        if (RequestTimeout <= TimeSpan.Zero)
        {
            yield return new ValidationResult(
                $"{nameof(RequestTimeout)} must be greater than zero.",
                [nameof(RequestTimeout)]);
        }

        if (BaseBackoffDelay > TimeSpan.Zero && MaxBackoffDelay > TimeSpan.Zero && MaxBackoffDelay < BaseBackoffDelay)
        {
            yield return new ValidationResult(
                $"{nameof(MaxBackoffDelay)} ({MaxBackoffDelay}) must be greater than or equal to {nameof(BaseBackoffDelay)} ({BaseBackoffDelay}).",
                [nameof(MaxBackoffDelay), nameof(BaseBackoffDelay)]);
        }
    }
}
