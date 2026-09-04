using System.ComponentModel.DataAnnotations;

namespace SharedKernel.Idempotency.EfCore.Options;

/// <summary>
/// Configuration options for <see cref="SharedKernel.Idempotency.EfCore.KeyStore.EfCoreIdempotencyKeyStore"/>.
/// </summary>
/// <remarks>
/// Registered via <c>AddSharedKernelEfCoreIdempotency</c>, which validates this options instance
/// eagerly at <c>IHost.StartAsync()</c> (never at first use) via
/// <c>ValidateDataAnnotations().ValidateOnStart()</c> — the same fail-fast mechanism
/// <c>01.Core/SharedKernel.Configuration</c>'s <c>AddValidatedOptions</c> promotes; this package
/// configures it directly (mirroring <c>SharedKernel.Idempotency.Redis</c>) because its DI surface
/// is action-based (<c>Action&lt;EfCoreIdempotencyOptions&gt;</c>), not
/// <see cref="Microsoft.Extensions.Configuration.IConfigurationSection"/>-based.
/// </remarks>
public sealed class EfCoreIdempotencyOptions : IValidatableObject
{
    /// <summary>The DI configuration section name for <see cref="EfCoreIdempotencyOptions"/>.</summary>
    public const string SectionName = "SharedKernel:Idempotency:EfCore";

    /// <summary>
    /// How long a fresh reservation lives before it self-expires if never confirmed via
    /// <c>MarkProcessedAsync</c> (Domain Invariant 2 — a fault must not consume the key). Defaults
    /// to 30 seconds.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:00.001", "1.00:00:00", ErrorMessage = "InFlightTtl must be greater than zero and at most 24 hours.")]
    public TimeSpan InFlightTtl { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long a confirmed (processed) key-store entry is retained after
    /// <c>MarkProcessedAsync</c> extends its expiry. Defaults to 24 hours.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:01", "365.00:00:00", ErrorMessage = "RetentionWindow must be between 1 second and 365 days.")]
    public TimeSpan RetentionWindow { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// When <see langword="true"/>, a genuine PostgreSQL connectivity/timeout failure causes the
    /// guarded call to proceed as if the key had not yet been processed, instead of blocking it.
    /// Defaults to <see langword="false"/> (fail-closed).
    /// </summary>
    /// <remarks>
    /// ENABLING THIS OPTION INCREASES DUPLICATE-EXECUTION RISK: while the database is unreachable,
    /// every call — including genuine duplicates — is treated as novel and allowed to proceed.
    /// Only enable this for operations where executing twice is safer than blocking entirely
    /// (Domain Invariant 4).
    /// </remarks>
    public bool AllowExecutionOnStoreUnavailable { get; set; }

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (InFlightTtl >= RetentionWindow)
        {
            yield return new ValidationResult(
                $"{nameof(InFlightTtl)} ({InFlightTtl}) must be strictly less than {nameof(RetentionWindow)} ({RetentionWindow}).",
                [nameof(InFlightTtl), nameof(RetentionWindow)]);
        }
    }
}
