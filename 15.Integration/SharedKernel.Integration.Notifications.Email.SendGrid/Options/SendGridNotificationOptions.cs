using System.ComponentModel.DataAnnotations;

namespace SharedKernel.Integration.Notifications.Email.SendGrid.Options;

/// <summary>Provider-specific credential configuration for outbound SendGrid email delivery.</summary>
/// <remarks>
/// Bound to configuration section <c>"SharedKernel:Integration:Notifications:SendGrid"</c> and
/// registered via <c>AddSendGridEmailNotifications</c>. The shared retry/backoff/timeout/concurrency
/// knobs live on <c>NotificationDeliveryOptions</c> (registered by
/// <c>AddSharedKernelNotifications</c>) — never duplicated here.
/// </remarks>
public sealed class SendGridNotificationOptions
{
    /// <summary>The SendGrid API key used to authenticate outbound Mail Send API calls.</summary>
    [Required(AllowEmptyStrings = false)]
    public string ApiKey { get; set; } = string.Empty;
}
