using SharedKernel.Security.Abstractions.Claims;

namespace SharedKernel.Security.Totp.StepUp;

/// <summary>
/// Options for <see cref="TotpStepUpClaimsTransformation"/>, registered by <c>AddTotpStepUp</c>.
/// </summary>
/// <remarks>
/// A plain configure-delegate-populated POCO — NOT <c>AddValidatedOptions</c>-bound (no required
/// external value), mirroring <c>SharedKernel.Security.ApiKey.Options.ApiKeyAuthenticationOptions</c>/
/// <c>SharedKernel.Security.Mtls.Options.MtlsAuthenticationOptions</c>'s own un-validated shape, not
/// <c>SharedKernel.Security.Oidc.Options.SecurityOptions</c>'s Authority/Audience-validated shape
/// (WO-069, P-452).
/// </remarks>
public sealed class TotpStepUpOptions
{
    /// <summary>
    /// Gets or sets the claim type stamped onto the current principal after a fresh successful TOTP
    /// challenge.
    /// </summary>
    /// <remarks>
    /// Defaults to <see cref="SecurityClaimTypes.AuthenticationMethod"/> (<c>"amr"</c>) — the same
    /// claim type <c>SharedKernel.Security.Oidc</c>'s existing defensive <c>amr</c> reader
    /// (<c>ClaimMappingOptions.AmrClaimType</c>) already consumes, so the two can never silently drift
    /// apart.
    /// </remarks>
    public string AmrClaimType { get; set; } = SecurityClaimTypes.AuthenticationMethod;

    /// <summary>Gets or sets the claim value stamped for a successful TOTP step-up.</summary>
    /// <remarks>Defaults to <c>"otp"</c> — the standard OIDC Authentication Method Reference value for a one-time password.</remarks>
    public string AmrValue { get; set; } = "otp";

    /// <summary>
    /// Gets or sets how long a successful TOTP challenge continues to be observed as "fresh" by
    /// <see cref="TotpStepUpClaimsTransformation"/>.
    /// </summary>
    /// <remarks>Defaults to 15 minutes.</remarks>
    public TimeSpan ChallengeFreshnessWindow { get; set; } = TimeSpan.FromMinutes(15);
}
