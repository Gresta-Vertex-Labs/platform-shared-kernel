using System.Security.Cryptography.X509Certificates;

namespace SharedKernel.Security.Mtls.Validation;

/// <summary>Decides which client a certificate belongs to, after the chain, revocation, usage and validity checks pass.</summary>
/// <remarks>
/// Implemented by the consuming service, typically by looking up the certificate's thumbprint or subject in a client
/// registry. Reject certificates that are trusted but not registered: a trusted authority may issue certificates to
/// parties that are not clients of this service.
/// </remarks>
public interface IMtlsCertificateValidator
{
    /// <summary>Validates a client certificate.</summary>
    /// <param name="certificate">The certificate presented on the connection.</param>
    /// <param name="cancellationToken">A token to cancel the validation.</param>
    /// <returns>The client the certificate belongs to, or a failure.</returns>
    ValueTask<MtlsValidationResult> ValidateAsync(X509Certificate2 certificate, CancellationToken cancellationToken);
}
