using System.ComponentModel.DataAnnotations;

namespace SharedKernel.Application.Pipeline.Idempotency;

/// <summary>
/// How long <see cref="IdempotencyBehavior{TRequest,TResponse}"/> holds a reservation and how long a completed key
/// replays its response.
/// </summary>
/// <remarks>
/// Configured through <c>ApplicationBehaviorsBuilder.AddIdempotencyBehavior(o =&gt; …)</c> and validated when the host
/// starts. Both values are passed to the <see cref="SharedKernel.Idempotency.Abstractions.IIdempotencyStore"/> on every
/// call, so the store registered for <see cref="SharedKernel.Idempotency.Abstractions.IdempotencyPurpose.Request"/>
/// needs no retention settings of its own.
/// </remarks>
public sealed class IdempotencyBehaviorOptions : IValidatableObject
{
    /// <summary>
    /// How long a reservation holds before it expires if the handler never finishes (a crashed process). Must outlast
    /// the slowest handler: once it expires, a retry with the same key runs the handler again.
    /// </summary>
    /// <value>Defaults to 30 seconds; between 1 millisecond and 24 hours.</value>
    [Range(typeof(TimeSpan), "00:00:00.001", "1.00:00:00", ErrorMessage = "LeaseDuration must be greater than zero and at most 24 hours.")]
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How long a completed key replays its stored response to a duplicate submission.</summary>
    /// <value>Defaults to 24 hours; between 1 second and 365 days.</value>
    [Range(typeof(TimeSpan), "00:00:01", "365.00:00:00", ErrorMessage = "RetentionWindow must be between 1 second and 365 days.")]
    public TimeSpan RetentionWindow { get; set; } = TimeSpan.FromHours(24);

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (LeaseDuration >= RetentionWindow)
        {
            yield return new ValidationResult(
                $"{nameof(LeaseDuration)} ({LeaseDuration}) must be strictly less than {nameof(RetentionWindow)} ({RetentionWindow}).",
                [nameof(LeaseDuration), nameof(RetentionWindow)]);
        }
    }
}
