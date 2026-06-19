using Microsoft.Extensions.Options;

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
    /// Overrides the <c>name</c> parameter for DNS lookup via <c>IServiceEndpointResolver</c>.
    /// Use when the typed client's logical registration name differs from its DNS service name.
    /// When <c>null</c> (default), the <c>name</c> parameter passed to
    /// <c>AddRestClient&lt;TClient&gt;</c> is used for DNS resolution.
    /// </summary>
    public string? ServiceName { get; set; }

    /// <summary>
    /// Per-request timeout in seconds applied via <c>StandardResilienceHandler</c>.
    /// Default: 30 s.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Resilience pipeline settings (retry, circuit breaker).</summary>
    public RestResilienceOptions Resilience { get; set; } = new();
}

/// <summary>
/// Validates <see cref="RestClientOptions"/> for clients that do not use service discovery.
/// BaseAddress is required unless the caller has registered an IServiceEndpointResolver separately.
/// The endpoint-resolver check cannot be done here (no DI access) — it is enforced in
/// <c>RestCommunicationBuilder.AddRestClient&lt;TClient&gt;</c> at registration time.
/// </summary>
internal sealed class RestClientOptionsValidator : IValidateOptions<RestClientOptions>
{
    public ValidateOptionsResult Validate(string? name, RestClientOptions options)
    {
        // BaseAddress emptiness validation only — resolver presence is checked in the builder.
        // An explicit empty string is suspicious; null means "use service discovery".
        if (options.BaseAddress is not null && string.IsNullOrWhiteSpace(options.BaseAddress))
        {
            return ValidateOptionsResult.Fail(
                $"RestClientOptions.BaseAddress must not be an empty or whitespace string. " +
                $"Set it to a valid URI or leave it null to use IServiceEndpointResolver.");
        }

        if (options.TimeoutSeconds <= 0)
        {
            return ValidateOptionsResult.Fail(
                $"RestClientOptions.TimeoutSeconds must be greater than zero. Got: {options.TimeoutSeconds}.");
        }

        return ValidateOptionsResult.Success;
    }
}
