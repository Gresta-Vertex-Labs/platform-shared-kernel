using System.ComponentModel.DataAnnotations;

namespace SharedKernel.Integration.Notifications.Sms.Twilio.Options;

/// <summary>Provider-specific credential and sender configuration for outbound Twilio SMS delivery.</summary>
/// <remarks>
/// Bound to configuration section <c>"SharedKernel:Integration:Notifications:Twilio"</c> and
/// registered via <c>AddTwilioSmsNotifications</c>. The shared retry/backoff/timeout/concurrency
/// knobs live on <c>NotificationDeliveryOptions</c> (registered by
/// <c>AddSharedKernelNotifications</c>) — never duplicated here.
/// </remarks>
public sealed class TwilioNotificationOptions : IValidatableObject
{
    /// <summary>The Twilio Account SID used for HTTP Basic authentication.</summary>
    [Required(AllowEmptyStrings = false)]
    public string AccountSid { get; set; } = string.Empty;

    /// <summary>The Twilio Auth Token used for HTTP Basic authentication.</summary>
    [Required(AllowEmptyStrings = false)]
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>
    /// The sending phone number/short-code/alphanumeric sender ID. At least one of
    /// <see cref="From"/> or <see cref="MessagingServiceSid"/> must be configured.
    /// </summary>
    public string? From { get; set; }

    /// <summary>
    /// The Twilio Messaging Service SID (enables sender pooling/geo-matching). At least one of
    /// <see cref="From"/> or <see cref="MessagingServiceSid"/> must be configured.
    /// </summary>
    public string? MessagingServiceSid { get; set; }

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrEmpty(From) && string.IsNullOrEmpty(MessagingServiceSid))
        {
            yield return new ValidationResult(
                $"At least one of {nameof(From)} or {nameof(MessagingServiceSid)} must be configured.",
                [nameof(From), nameof(MessagingServiceSid)]);
        }
    }
}
