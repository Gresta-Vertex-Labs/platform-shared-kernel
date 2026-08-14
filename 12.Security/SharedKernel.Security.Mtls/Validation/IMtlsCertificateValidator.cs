using System.Security.Cryptography.X509Certificates;

namespace SharedKernel.Security.Mtls.Validation;

/// <summary>
/// A consumer-supplied validator for a presented mutual-TLS client certificate.
/// </summary>
/// <remarks>
/// <para>
/// The sole consumer-supplied extensibility point — mirrors <c>SharedKernel.Security.ApiKey.Validation.IApiKeyValidator</c>
/// exactly. This package performs NO CA/chain/revocation (CRL/OCSP) validation of its own beyond what is
/// delegated to it; the consuming service decides trust-store and revocation policy entirely (WO-058, P-377).
/// </para>
/// </remarks>
public interface IMtlsCertificateValidator
{
    /// <summary>
    /// Validates the presented client certificate.
    /// </summary>
    /// <param name="certificate">The client certificate presented on the current connection.</param>
    /// <param name="ct">A token to observe for cancellation.</param>
    /// <returns>The validation outcome.</returns>
    Task<MtlsValidationResult> ValidateAsync(X509Certificate2 certificate, CancellationToken ct);
}
