namespace SharedKernel.Integration.Notifications.Abstractions.Notifications;

/// <summary>
/// The delivery channel a <see cref="NotificationMessage{TTemplateModel}"/> is sent through.
/// </summary>
/// <remarks>
/// No <c>Push</c> member — device-token registration and platform-specific payload shaping are
/// materially more scope than text delivery and are explicitly declined for this package's first
/// release (WO-072). Propose it as its own follow-up once this seam is proven.
/// </remarks>
public enum NotificationChannel
{
    /// <summary>Email delivery — see <c>SharedKernel.Integration.Notifications.Email.SendGrid</c>.</summary>
    Email,

    /// <summary>SMS delivery — see <c>SharedKernel.Integration.Notifications.Sms.Twilio</c>.</summary>
    Sms,
}
