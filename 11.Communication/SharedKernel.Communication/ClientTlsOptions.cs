using System.ComponentModel.DataAnnotations;

namespace SharedKernel.Communication;

/// <summary>
/// TLS for one client: the certificate it presents for mutual TLS and the certificate authorities it trusts the
/// server by. Files are read whenever the connection handler is rebuilt (every two minutes by default), so a certificate
/// rotated on disk — by cert-manager, for instance — is picked up without a restart.
/// </summary>
/// <example>
/// <code>
/// "Tls": {
///   "CertificatePath": "/var/run/secrets/tls/tls.crt",
///   "PrivateKeyPath": "/var/run/secrets/tls/tls.key",
///   "TrustedCertificateAuthoritiesPath": "/var/run/secrets/tls/ca.crt"
/// }
/// </code>
/// </example>
public sealed class ClientTlsOptions
{
    /// <summary>
    /// Gets or sets the client certificate for mutual TLS: a PEM certificate (with <see cref="PrivateKeyPath"/>) or a
    /// PKCS#12 (<c>.pfx</c>/<c>.p12</c>) file holding its key.
    /// </summary>
    public string? CertificatePath { get; set; }

    /// <summary>Gets or sets the PEM private key of a PEM <see cref="CertificatePath"/>.</summary>
    public string? PrivateKeyPath { get; set; }

    /// <summary>Gets or sets the password of a PKCS#12 file or of an encrypted PEM private key.</summary>
    public string? CertificatePassword { get; set; }

    /// <summary>
    /// Gets or sets a PEM file of certificate authorities. When set, the server's certificate must chain to one of them
    /// — and only them, not the machine's trust store: the setting for a private PKI. Revocation is not checked, since
    /// private authorities rarely publish revocation lists.
    /// </summary>
    public string? TrustedCertificateAuthoritiesPath { get; set; }

    internal bool HasClientCertificate => !string.IsNullOrWhiteSpace(CertificatePath);

    internal IEnumerable<ValidationResult> Validate()
    {
        const string Prefix = nameof(CommunicationClientOptions.Tls);

        if (!string.IsNullOrWhiteSpace(PrivateKeyPath) && !HasClientCertificate)
        {
            yield return new ValidationResult($"{Prefix}:PrivateKeyPath is set without a CertificatePath.", [Prefix]);
        }

        foreach (var (name, path) in new[]
                 {
                     (nameof(CertificatePath), CertificatePath),
                     (nameof(PrivateKeyPath), PrivateKeyPath),
                     (nameof(TrustedCertificateAuthoritiesPath), TrustedCertificateAuthoritiesPath),
                 })
        {
            if (!string.IsNullOrWhiteSpace(path) && !File.Exists(path))
            {
                yield return new ValidationResult($"{Prefix}:{name} '{path}' does not exist.", [Prefix]);
            }
        }
    }
}
