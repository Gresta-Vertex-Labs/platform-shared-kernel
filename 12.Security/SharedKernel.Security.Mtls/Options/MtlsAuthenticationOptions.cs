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
    /// Defaults to <see cref="CertificateTypes.All"/> — this package delegates full trust-store and
    /// revocation policy to <see cref="Validation.IMtlsCertificateValidator"/> rather than pre-filtering
    /// via the framework's own default chained-only requirement.
    /// </remarks>
    public CertificateTypes AllowedCertificateTypes { get; set; } = CertificateTypes.All;

    /// <summary>
    /// Gets or sets the ASP.NET Core Certificate authentication handler's certificate revocation-check
    /// mode.
    /// </summary>
    /// <remarks>
    /// Defaults to <see cref="X509RevocationMode.NoCheck"/> — this package performs no revocation
    /// checking of its own; a consuming <see cref="Validation.IMtlsCertificateValidator"/> that needs
    /// CRL/OCSP checking implements it itself.
    /// </remarks>
    public X509RevocationMode RevocationMode { get; set; } = X509RevocationMode.NoCheck;
}
