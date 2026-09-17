using System.Buffers.Text;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace SharedKernel.Security.Oidc.Authentication;

// RFC 8705 section 3: a certificate-bound token is accepted only over a connection authenticated with the same
// client certificate. The certificate comes from the TLS connection, or from the forwarding middleware when TLS
// terminates at a proxy.
internal static class CertificateBinding
{
    public static async Task<string?> ValidateAsync(HttpContext context, string expectedThumbprint)
    {
        X509Certificate2? certificate = await context.Connection
            .GetClientCertificateAsync(context.RequestAborted)
            .ConfigureAwait(false);

        if (certificate is null)
        {
            return "CertificateMissing";
        }

        string actual = Base64Url.EncodeToString(SHA256.HashData(certificate.RawDataMemory.Span));
        bool matches = CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(actual),
            Encoding.ASCII.GetBytes(expectedThumbprint));

        return matches ? null : "CertificateMismatch";
    }
}
