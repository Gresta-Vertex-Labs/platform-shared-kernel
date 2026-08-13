using Microsoft.AspNetCore.Authentication;

namespace SharedKernel.Security.ApiKey.Options;

/// <summary>
/// Options for the API-key authentication scheme registered by <c>AddApiKeyAuthentication</c>.
/// </summary>
public sealed class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    /// <summary>The name of the API-key authentication scheme.</summary>
    public const string DefaultScheme = "ApiKey";

    /// <summary>
    /// The name of the composite (policy/forwarding) authentication scheme registered alongside the JWT
    /// Bearer scheme, so a host can accept either credential type on the same set of endpoints.
    /// </summary>
    public const string CompositeSchemeName = "SharedKernel.Composite";

    /// <summary>
    /// Gets or sets the HTTP request header name the API key is read from.
    /// </summary>
    /// <remarks>Defaults to <c>"X-Api-Key"</c>.</remarks>
    public string HeaderName { get; set; } = "X-Api-Key";

    /// <summary>
    /// Gets or sets an optional query-string parameter name the API key may alternatively be read from.
    /// </summary>
    /// <remarks>
    /// <see langword="null"/> by default (query-parameter fallback disabled). When both the header and
    /// the query parameter are present on the same request and their values differ, authentication fails
    /// (a possible credential-confusion attack) — see
    /// <see cref="SharedKernel.Security.ApiKey.Validation.ApiKeyAuthenticationHandler"/>.
    /// </remarks>
    public string? QueryParameterName { get; set; }
}
