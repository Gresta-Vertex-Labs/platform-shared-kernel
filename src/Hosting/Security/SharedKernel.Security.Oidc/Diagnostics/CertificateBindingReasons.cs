namespace SharedKernel.Security.Oidc.Diagnostics;

// Closed, log-only rejection codes of the RFC 8705 certificate-binding check (event 12105).
internal static class CertificateBindingReasons
{
    internal const string CertificateMissing = "CertificateMissing";
    internal const string CertificateMismatch = "CertificateMismatch";
}
