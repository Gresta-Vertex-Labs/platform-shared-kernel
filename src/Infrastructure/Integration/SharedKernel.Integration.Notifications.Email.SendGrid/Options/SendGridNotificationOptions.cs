using System.ComponentModel.DataAnnotations;

namespace SharedKernel.Integration.Notifications.Email.SendGrid.Options;

/// <summary>Provider-specific credential configuration for outbound SendGrid email delivery.</summary>
/// <remarks>
/// Bound to configuration section <c>"SharedKernel:Integration:Notifications:SendGrid"</c> and
/// registered via <c>AddSendGridEmailNotifications</c>. The shared retry/backoff/timeout/concurrency
/// knobs live on <c>NotificationDeliveryOptions</c> (registered by
/// <c>AddSharedKernelNotifications</c>) — never duplicated here.
/// </remarks>
public sealed class SendGridNotificationOptions : IValidatableObject
{
    /// <summary>The default <see cref="BaseAddress"/>: the SendGrid v3 API.</summary>
    public static readonly Uri DefaultBaseAddress = new("https://api.sendgrid.com/");

    /// <summary>The SendGrid API key used to authenticate outbound Mail Send API calls.</summary>
    [Required(AllowEmptyStrings = false)]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// The base address the Mail Send call (<c>v3/mail/send</c>) is resolved against. Defaults to
    /// <see cref="DefaultBaseAddress"/>; change it only to reach a stand-in such as a WireMock server in local and
    /// end-to-end environments. The API key is sent to this address, so it must be one you control.
    /// </summary>
    [Required]
    public Uri BaseAddress { get; set; } = DefaultBaseAddress;

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (BaseAddress is { IsAbsoluteUri: false })
        {
            yield return new ValidationResult($"{nameof(BaseAddress)} must be an absolute URI.", [nameof(BaseAddress)]);
        }
    }
}
