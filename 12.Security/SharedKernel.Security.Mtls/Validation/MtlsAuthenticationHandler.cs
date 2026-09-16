using System.Buffers.Text;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.Certificate;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedKernel.Cryptography;
using SharedKernel.Cryptography.Hashing;
using SharedKernel.Security.Mtls.Logging;

namespace SharedKernel.Security.Mtls.Validation;

/// <summary>
/// Authenticates a request presenting a mutual-TLS client certificate, delegating the actual trust
/// decision to a consumer-supplied <see cref="IMtlsCertificateValidator"/>, and enforcing the RFC 8705
/// <c>cnf.x5t#S256</c> certificate-bound access-token binding when a bearer token accompanies the
/// certificate.
/// </summary>
/// <remarks>
/// <para>
/// Wraps <c>Microsoft.AspNetCore.Authentication.Certificate</c>'s <see cref="CertificateAuthenticationEvents.OnCertificateValidated"/>
/// event — this package performs no CA/chain/revocation validation of its own beyond what
/// <see cref="IMtlsCertificateValidator"/> delegates to it (WO-058, P-377).
/// </para>
/// <para>
/// The certificate-binding check hashes the presented certificate with the <see cref="IContentHasher"/>
/// registered by <c>AddSharedKernelCryptography</c> and compares thumbprints with
/// <see cref="FixedTimeComparison.AreEqual(string, string)"/>.
/// </para>
/// </remarks>
internal static class MtlsAuthenticationHandler
{
    private const string ConfirmationClaimType = "cnf";
    private const string X5tS256PropertyName = "x5t#S256";

    /// <summary>
    /// Handles a successfully chain-validated client certificate: delegates the trust decision to the
    /// registered <see cref="IMtlsCertificateValidator"/>, enforces the RFC 8705 certificate-binding
    /// check when applicable, and builds the resulting <see cref="ClaimsPrincipal"/> on success.
    /// </summary>
    /// <param name="context">The <c>OnCertificateValidated</c> context for the current request.</param>
    internal static async Task HandleCertificateValidatedAsync(CertificateValidatedContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var services = context.HttpContext.RequestServices;
        var logger = services.GetService<ILogger<IMtlsCertificateValidator>>();
        var validator = services.GetRequiredService<IMtlsCertificateValidator>();

        var result = await validator.ValidateAsync(context.ClientCertificate, context.HttpContext.RequestAborted).ConfigureAwait(false);
        if (!result.IsValid)
        {
            Reject(context, logger, "Rejected");
            return;
        }

        if (!TryVerifyCertificateBinding(context, services, out var bindingFailureReason))
        {
            Reject(context, logger, bindingFailureReason!);
            return;
        }

        var identity = new ClaimsIdentity(BuildClaims(result), context.Scheme.Name);
        context.Principal = new ClaimsPrincipal(identity);
        context.Success();
    }

    private static bool TryVerifyCertificateBinding(
        CertificateValidatedContext context,
        IServiceProvider services,
        out string? failureReason)
    {
        failureReason = null;

        // Only meaningful when a bearer token accompanies the certificate on the same request — a
        // certificate-only request has nothing to bind against.
        var cnfClaim = context.HttpContext.User.FindFirst(ConfirmationClaimType);
        if (cnfClaim is null)
        {
            return true;
        }

        string? expectedThumbprint;
        try
        {
            using var cnfDoc = JsonDocument.Parse(cnfClaim.Value);
            expectedThumbprint = cnfDoc.RootElement.TryGetProperty(X5tS256PropertyName, out var thumbprintElement)
                ? thumbprintElement.GetString()
                : null;
        }
        catch (JsonException)
        {
            failureReason = "MalformedConfirmation";
            return false;
        }

        if (string.IsNullOrEmpty(expectedThumbprint))
        {
            return true;
        }

        var contentHasher = services.GetRequiredService<IContentHasher>();
        var actualThumbprintBytes = contentHasher.ComputeHash(context.ClientCertificate.RawDataMemory.Span);
        var actualThumbprint = Base64Url.EncodeToString(actualThumbprintBytes);

        if (!FixedTimeComparison.AreEqual(actualThumbprint, expectedThumbprint))
        {
            failureReason = "CnfMismatch";
            return false;
        }

        return true;
    }

    private static IEnumerable<Claim> BuildClaims(MtlsValidationResult result)
    {
        if (!string.IsNullOrEmpty(result.ClientId))
        {
            yield return new Claim(MtlsClaimTypes.ClientId, result.ClientId);
        }

        if (result.Roles is not null)
        {
            foreach (var role in result.Roles)
            {
                yield return new Claim(MtlsClaimTypes.Role, role);
            }
        }

        if (result.Permissions is not null)
        {
            foreach (var permission in result.Permissions)
            {
                yield return new Claim(MtlsClaimTypes.Permission, permission);
            }
        }
    }

    private static void Reject(CertificateValidatedContext context, ILogger? logger, string reason)
    {
        if (logger is not null)
        {
            SecurityLogEvents.MtlsCertificateRejected(logger, reason);
        }

        context.Fail($"Client certificate rejected: {reason}");
    }
}
