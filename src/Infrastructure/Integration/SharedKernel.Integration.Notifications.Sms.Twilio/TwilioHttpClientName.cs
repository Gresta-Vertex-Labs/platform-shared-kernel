namespace SharedKernel.Integration.Notifications.Sms.Twilio;

/// <summary>Holds the name of the named <see cref="System.Net.Http.HttpClient"/> used for Twilio delivery.</summary>
internal static class TwilioHttpClientName
{
    /// <summary>
    /// The name passed to <c>IHttpClientFactory.CreateClient(string)</c> for outbound Twilio
    /// delivery, registered by <c>AddTwilioSmsNotifications</c> via
    /// <c>AddHttpClient(...).AddStandardResilienceHandler(...)</c>.
    /// </summary>
    public const string Name = "SharedKernel.Integration.Notifications.Sms.Twilio";
}
