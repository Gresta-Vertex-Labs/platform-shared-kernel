using Microsoft.AspNetCore.Authentication.Certificate;
using System.Security.Cryptography.X509Certificates;

namespace SharedKernel.Security.Mtls.Options;

/// <summary>
/// Options for the mutual-TLS client-certificate authentication scheme registered by
/// <c>AddMtlsAuthentication</c>.
/// </summary>
public sealed class MtlsAuthenticationOptions
{
    /// <summary>The name of the mutual-TLS authentication scheme.</summary>
    public const string DefaultScheme = "Certificate";

    /// <summary>
    /// Gets or sets which certificate types the underlying ASP.NET Core Certificate authentication
    /// handler accepts before <see cref="Validation.IMtlsCertificateValidator"/> ever runs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Defaults to <see cref="CertificateTypes.Chained"/> — a self-signed certificate is rejected at the
    /// framework's own pre-filter stage, before <see cref="Validation.IMtlsCertificateValidator"/> ever
    /// gets a chance to evaluate it. This is the ASP.NET Core framework's own default; a consuming
    /// service following this package's own documented common path
    /// (<c>AddMtlsAuthentication&lt;TValidator&gt;()</c> with no options override) gets a starting
    /// posture no weaker than using <c>Microsoft.AspNetCore.Authentication.Certificate</c> directly
    /// (WO-060, P-386).
    /// </para>
    /// <para>
    /// <b>OVERRIDING THIS TO <see cref="CertificateTypes.All"/> (OR ANY VALUE ADMITTING SELF-SIGNED
    /// CERTIFICATES) WEAKENS TRUST VALIDATION.</b> Only opt into this for a legitimate reason — e.g. a
    /// private-PKI Open Banking QWAC/QSEAL trust chain that is not a standard public CA — and do so
    /// explicitly via <c>configureOptions</c>, never as a blanket default.
    /// </para>
    /// </remarks>
    public CertificateTypes AllowedCertificateTypes { get; set; } = CertificateTypes.Chained;

    /// <summary>
    /// Gets or sets the ASP.NET Core Certificate authentication handler's certificate revocation-check
    /// mode.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Defaults to <see cref="X509RevocationMode.Offline"/> — checks locally cached CRL data, a
    /// materially stronger posture than <see cref="X509RevocationMode.NoCheck"/> with no hidden outbound
    /// network dependency imposed as the default for every consumer (chosen over
    /// <see cref="X509RevocationMode.Online"/> for exactly that reason — a shared-kernel library must not
    /// impose a live network round-trip on every consumer's default configuration). This package
    /// otherwise performs no revocation checking of its own; a consuming
    /// <see cref="Validation.IMtlsCertificateValidator"/> that needs additional CRL/OCSP checking
    /// implements it itself (WO-060, P-386).
    /// </para>
    /// <para>
    /// <b>OVERRIDING THIS TO <see cref="X509RevocationMode.NoCheck"/> WEAKENS TRUST VALIDATION.</b> Only
    /// opt into this for a legitimate, explicit reason via <c>configureOptions</c>, never as a blanket
    /// default.
    /// </para>
    /// </remarks>
    public X509RevocationMode RevocationMode { get; set; } = X509RevocationMode.Offline;
}
