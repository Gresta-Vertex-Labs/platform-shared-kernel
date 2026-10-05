namespace SharedKernel.Integration.Notifications.Sms.Twilio;

/// <summary>Single source of truth for the HTTP header names this provider reads or writes.</summary>
internal static class TwilioHeaders
{
    /// <summary>
    /// Twilio's documented Messages API idempotency header — a genuine, provider-enforced
    /// request-level dedup guarantee. Carries <c>NotificationDeliveryId</c> on every send.
    /// </summary>
    public const string IdempotencyKeyHeaderName = "Idempotency-Key";
}
