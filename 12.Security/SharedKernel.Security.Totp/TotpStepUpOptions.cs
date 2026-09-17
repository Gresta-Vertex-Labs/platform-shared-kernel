using SharedKernel.Security.Abstractions;

namespace SharedKernel.Security.Totp;

/// <summary>Options for TOTP step-up authentication.</summary>
public sealed class TotpStepUpOptions
{
    /// <summary>
    /// Gets or sets how long a successful code or recovery code counts as a step-up for the session, from one minute to
    /// 24 hours. Defaults to 15 minutes.
    /// </summary>
    public TimeSpan FreshnessWindow { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Gets or sets the claim type added to the caller's identity. Defaults to <c>amr</c>; it must match the claim the
    /// authentication package reads authentication methods from.
    /// </summary>
    public string AuthenticationMethodClaimType { get; set; } = SecurityClaimTypes.AuthenticationMethod;

    /// <summary>Gets or sets the authentication method added (RFC 8176). Defaults to <c>otp</c>.</summary>
    public string AuthenticationMethod { get; set; } = "otp";
}
