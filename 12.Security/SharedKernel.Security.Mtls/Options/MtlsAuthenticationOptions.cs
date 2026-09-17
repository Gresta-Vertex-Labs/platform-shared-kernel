using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Authentication.Certificate;

namespace SharedKernel.Security.Mtls.Options;

/// <summary>How client certificates are checked before <see cref="Validation.IMtlsCertificateValidator"/> runs.</summary>
/// <remarks>The defaults match ASP.NET Core's certificate authentication and are never weaker.</remarks>
public sealed class MtlsAuthenticationOptions
{
    /// <summary>
    /// Gets or sets which certificates are accepted. Defaults to <see cref="CertificateTypes.Chained"/>: a certificate
    /// must chain to a trusted root. Allowing self-signed certificates hands all trust to the validator.
    /// </summary>
    public CertificateTypes AllowedCertificateTypes { get; set; } = CertificateTypes.Chained;

    /// <summary>
    /// Gets or sets which roots are trusted. Defaults to <see cref="X509ChainTrustMode.System"/>. Use
    /// <see cref="X509ChainTrustMode.CustomRootTrust"/> with <see cref="CustomTrustStore"/> for a private certificate
    /// authority, instead of allowing self-signed certificates.
    /// </summary>
    public X509ChainTrustMode ChainTrustValidationMode { get; set; } = X509ChainTrustMode.System;

    /// <summary>
    /// Gets the roots, and any intermediates, trusted when <see cref="ChainTrustValidationMode"/> is
    /// <see cref="X509ChainTrustMode.CustomRootTrust"/>.
    /// </summary>
    public X509Certificate2Collection CustomTrustStore { get; } = [];

    /// <summary>
    /// Gets or sets how revocation is checked. Defaults to <see cref="X509RevocationMode.Online"/>, which downloads
    /// CRLs or queries OCSP. A certificate whose revocation status cannot be determined is rejected; for a private
    /// authority without CRL distribution points set <see cref="X509RevocationMode.NoCheck"/> and revoke clients in
    /// the validator.
    /// </summary>
    public X509RevocationMode RevocationMode { get; set; } = X509RevocationMode.Online;

    /// <summary>Gets or sets which certificates in the chain are checked for revocation. Defaults to <see cref="X509RevocationFlag.ExcludeRoot"/>.</summary>
    public X509RevocationFlag RevocationFlag { get; set; } = X509RevocationFlag.ExcludeRoot;

    /// <summary>
    /// Gets or sets a value indicating whether the certificate must allow client authentication (extended key usage
    /// 1.3.6.1.5.5.7.3.2). Defaults to <see langword="true"/>.
    /// </summary>
    public bool ValidateCertificateUse { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether an expired or not yet valid certificate is rejected. Defaults to <see langword="true"/>.</summary>
    public bool ValidateValidityPeriod { get; set; } = true;
}
