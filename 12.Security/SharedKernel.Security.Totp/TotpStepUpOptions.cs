using SharedKernel.Security.Abstractions;

namespace SharedKernel.Security.Totp;

/// <summary>Options for TOTP step-up authentication.</summary>
public sealed class TotpStepUpOptions
{
    /// <summary>
    /// Gets or sets how long a successful code or recovery code counts as a step-up for the session, from one minute to
    /// 24 hours. Defaults to 15 minutes.
    /// </summary>
    /// <remarks>
    /// The window is applied when a request authenticates. A SignalR connection authenticates once, when it opens, and
    /// keeps the method for as long as it stays open; there, a requirement with a maximum age no longer than this window
    /// (<c>[RequireAuthenticationMethod("otp", MaxAgeSeconds = …)]</c>) ends the step-up at the same time as over HTTP.
    /// </remarks>
    public TimeSpan FreshnessWindow { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Gets or sets the claim type added to the caller's identity. Defaults to <c>amr</c>; it must match the claim the
    /// authentication package reads authentication methods from.
    /// </summary>
    public string AuthenticationMethodClaimType { get; set; } = SecurityClaimTypes.AuthenticationMethod;

    /// <summary>Gets or sets the authentication method added (RFC 8176). Defaults to <c>otp</c>.</summary>
    public string AuthenticationMethod { get; set; } = "otp";
}
