namespace SharedKernel.Integration.Webhooks.Dispatch;

/// <summary>Holds the name of the named <see cref="System.Net.Http.HttpClient"/> used for webhook delivery.</summary>
internal static class WebhookHttpClientName
{
    /// <summary>
    /// The name passed to <c>IHttpClientFactory.CreateClient(string)</c> for outbound webhook delivery,
    /// registered by <c>AddSharedKernelWebhooks</c> via
    /// <c>AddHttpClient(...).AddStandardResilienceHandler(...)</c>.
    /// </summary>
    public const string Name = "SharedKernel.Integration.Webhooks";
}
