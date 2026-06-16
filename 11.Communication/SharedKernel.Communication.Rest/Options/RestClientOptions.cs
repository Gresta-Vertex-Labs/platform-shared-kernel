namespace SharedKernel.Communication.Rest.Options;

/// <summary>
/// Configuration for a typed REST client registered via
/// <c>IRestCommunicationBuilder.AddRestClient&lt;TClient&gt;</c>.
/// </summary>
public sealed class RestClientOptions
{
    /// <summary>
    /// Base address for the typed client. Required when <c>IServiceEndpointResolver</c> is not
    /// registered in the DI container; omit to enable at-request-time service-discovery resolution.
    /// </summary>
    public string? BaseAddress { get; set; }

    /// <summary>
    /// Per-request timeout in seconds applied via <c>StandardResilienceHandler</c>.
    /// Default: 30 s.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Resilience pipeline settings (retry, circuit breaker).</summary>
    public RestResilienceOptions Resilience { get; set; } = new();
}
