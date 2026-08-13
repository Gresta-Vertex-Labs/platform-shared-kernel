using System.ComponentModel.DataAnnotations;

namespace SharedKernel.Security.Oidc.Options;

/// <summary>
/// Configuration options for JWT Bearer authentication.
/// Bound from the <c>Security</c> configuration section via <c>AddValidatedOptions</c>.
/// Misconfigured applications fail at startup (IHost.StartAsync), not at first authentication.
/// </summary>
public sealed class SecurityOptions
{
    /// <summary>
    /// The configuration section key used to bind <see cref="SecurityOptions"/>.
    /// </summary>
    public const string SectionKey = "Security";

    /// <summary>Gets or sets the JWT Bearer token validation options.</summary>
    [Required]
    public JwtOptions Jwt { get; set; } = new();

    /// <summary>
    /// Gets or sets the claim-type mapping used to resolve <see cref="Mapping.OidcUserContext"/>'s
    /// email/username/roles/permissions from the current <see cref="System.Security.Claims.ClaimsPrincipal"/>.
    /// </summary>
    /// <remarks>Added WO-057 (P-366). See <see cref="ClaimMappingOptions"/> for the full rationale.</remarks>
    [Required]
    public ClaimMappingOptions ClaimMapping { get; set; } = new();

    /// <summary>
    /// JWT Bearer token validation configuration.
    /// </summary>
    public sealed class JwtOptions
    {
        /// <summary>
        /// Gets or sets the OIDC authority URL (e.g. <c>https://login.microsoftonline.com/{tenantId}/v2.0</c>).
        /// </summary>
        /// <remarks>Required. Application fails at startup when absent or empty.</remarks>
        [Required(AllowEmptyStrings = false)]
        public string Authority { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the expected audience (client ID or API scope URI) for token validation.
        /// </summary>
        /// <remarks>Required. Application fails at startup when absent or empty.</remarks>
        [Required(AllowEmptyStrings = false)]
        public string Audience { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets a value indicating whether token lifetime is validated.
        /// Defaults to <see langword="true"/>. Set to <see langword="false"/> only for development
        /// environments where long-lived tokens are used to avoid repeated re-authentication.
        /// </summary>
        public bool ValidateLifetime { get; set; } = true;

        /// <summary>
        /// Gets or sets the allowed clock skew in seconds when validating token expiry.
        /// Defaults to <c>30</c> seconds to tolerate minor clock drift between services.
        /// </summary>
        public int ClockSkewSeconds { get; set; } = 30;
    }
}
