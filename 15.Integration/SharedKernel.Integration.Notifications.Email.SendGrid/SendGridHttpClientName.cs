namespace SharedKernel.Integration.Notifications.Email.SendGrid;

/// <summary>Holds the name of the named <see cref="System.Net.Http.HttpClient"/> used for SendGrid delivery.</summary>
internal static class SendGridHttpClientName
{
    /// <summary>
    /// The name passed to <c>IHttpClientFactory.CreateClient(string)</c> for outbound SendGrid
    /// delivery, registered by <c>AddSendGridEmailNotifications</c> via
    /// <c>AddHttpClient(...).AddStandardResilienceHandler(...)</c>.
    /// </summary>
    public const string Name = "SharedKernel.Integration.Notifications.Email.SendGrid";
}
