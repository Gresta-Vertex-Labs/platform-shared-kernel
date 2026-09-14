using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.ServiceDefaults.Logging;
using SharedKernel.Security.Mtls.Validation;

namespace SharedKernel.ServiceDefaults.Security;

/// <summary>
/// Reads a mutual-TLS client certificate forwarded by an ingress/gateway as a request header,
/// validates it through the same <see cref="IMtlsCertificateValidator"/>
/// <see cref="MtlsClientCertificateExtensions.AddMtlsClientCertificate"/> uses for a
/// directly-negotiated certificate, and — on success — exposes it to downstream code exactly as a
/// directly-negotiated client certificate would be.
/// </summary>
/// <remarks>
/// <para>
/// Register via <see cref="MtlsForwardedHeaderExtensions.AddMtlsForwardedHeaderCertificate"/>, then
/// add this middleware explicitly:
/// <c>app.UseMiddleware&lt;MtlsForwardedHeaderMiddleware&gt;()</c> — registering the services alone
/// leaves this middleware absent from the pipeline, a silent no-op rather than a crash.
/// </para>
/// <para>
/// <b>Trust-boundary allowlist (WO-061/P-394):</b> when <see cref="MtlsForwardedHeaderOptions.TrustedNetworks"/>
/// is non-empty, a request whose <see cref="ConnectionInfo.RemoteIpAddress"/> falls outside every
/// configured network is rejected outright — the header is never decoded, never validated, and
/// <see cref="ConnectionInfo.ClientCertificate"/> is never set, regardless of whether the
/// certificate itself would otherwise validate.
/// </para>
/// <para>
/// <b>Decode shape:</b> the configured header's value is decoded as either a Base64-encoded DER
/// certificate (tried first — unambiguous once it parses, and the common minimal forwarding
/// convention, e.g. HAProxy's <c>%[ssl_c_der,base64]</c>) or, failing that, a URL-encoded PEM
/// certificate (the common nginx-ingress <c>$ssl_client_escaped_cert</c> convention) as a fallback.
/// <b>Envoy/Istio's structured <c>x-forwarded-client-cert</c> format
/// (<c>Hash=...;Cert="...";Chain="...";Subject=...</c>) is NOT parsed by this middleware</b> — a
/// host on that ingress must either configure its gateway to forward a single-value header carrying
/// only the certificate, or supply its own middleware.
/// </para>
/// <para>
/// On successful validation, the certificate is exposed via
/// <c>HttpContext.Connection.ClientCertificate</c> — confirmed directly against ASP.NET Core's
/// shipped implementation (<c>DefaultConnectionInfo</c> backs this property with a settable
/// <see cref="ITlsConnectionFeature"/>), the same property a directly-negotiated TLS client
/// certificate populates, so downstream code never needs to know which TLS-termination topology
/// produced it. The certificate is disposed automatically once the response completes.
/// </para>
/// <para>
/// A header that is absent, malformed, or rejected by <see cref="IMtlsCertificateValidator"/> never
/// throws — no certificate is set and the request proceeds unauthenticated for mTLS purposes;
/// downstream authorization, not this middleware, decides whether that is acceptable for a given
/// endpoint.
/// </para>
/// </remarks>
public sealed class MtlsForwardedHeaderMiddleware(
    RequestDelegate next,
    IOptions<MtlsForwardedHeaderOptions> options,
    ILogger<MtlsForwardedHeaderMiddleware> logger)
{
    /// <summary>Placeholder logged in place of a thumbprint/subject when a certificate was never decoded.</summary>
    private const string NotDecodedPlaceholder = "(not decoded)";

    /// <summary>
    /// Reads, decodes, and validates the forwarded client certificate for the current request
    /// (setting it on <c>HttpContext.Connection.ClientCertificate</c> when validation succeeds),
    /// then invokes the next middleware in the pipeline. Never throws for a missing, malformed,
    /// untrusted-source, or rejected certificate.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <param name="validator">The scoped <see cref="IMtlsCertificateValidator"/>.</param>
    public async Task InvokeAsync(HttpContext context, IMtlsCertificateValidator validator)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(validator);

        var configuredOptions = options.Value;
        var trustedNetworks = configuredOptions.TrustedNetworks;

        // Trust-boundary allowlist check (WO-061/P-394), mirroring the role ASP.NET Core's own
        // ForwardedHeadersOptions.KnownProxies/KnownNetworks plays for UseForwardedHeaders(): a
        // request from outside the configured allowlist is rejected here, before the header is ever
        // decoded — the certificate is never given a chance to "otherwise validate."
        if (trustedNetworks.Count > 0
            && !IsTrustedRemoteAddress(context.Connection.RemoteIpAddress, trustedNetworks))
        {
            MtlsLog.MtlsCertificateRejected(
                logger,
                NotDecodedPlaceholder,
                NotDecodedPlaceholder,
                $"remote IP '{context.Connection.RemoteIpAddress}' is not within the configured TrustedNetworks allowlist");

            await next(context).ConfigureAwait(false);
            return;
        }

        var headerName = configuredOptions.HeaderName;

        if (context.Request.Headers.TryGetValue(headerName, out var headerValues)
            && TryDecodeCertificate(headerValues.ToString(), out var certificate))
        {
            var result = await validator
                .ValidateAsync(certificate!, context.RequestAborted)
                .ConfigureAwait(false);

            if (result.IsValid)
            {
                context.Connection.ClientCertificate = certificate;
                context.Response.OnCompleted(() =>
                {
                    certificate!.Dispose();
                    return Task.CompletedTask;
                });
                MtlsLog.MtlsCertificateAccepted(logger, certificate!.Thumbprint, certificate.Subject);
            }
            else
            {
                MtlsLog.MtlsCertificateRejected(
                    logger,
                    certificate!.Thumbprint,
                    certificate.Subject,
                    "rejected by IMtlsCertificateValidator");
                certificate.Dispose();
            }
        }

        await next(context).ConfigureAwait(false);
    }

    /// <summary>
    /// Determines whether <paramref name="remoteIpAddress"/> falls within any network in
    /// <paramref name="trustedNetworks"/>. See <see cref="MtlsForwardedHeaderOptions.TrustedNetworks"/>.
    /// </summary>
    private static bool IsTrustedRemoteAddress(IPAddress? remoteIpAddress, IReadOnlyCollection<IPNetwork> trustedNetworks)
    {
        if (remoteIpAddress is null)
        {
            return false;
        }

        foreach (var network in trustedNetworks)
        {
            if (network.Contains(remoteIpAddress))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Attempts to decode a forwarded-header value into an <see cref="X509Certificate2"/>, trying
    /// Base64-encoded DER first and falling back to URL-encoded PEM. See the "Decode shape" remarks
    /// on this type for the vendor conventions this covers (and the one — Envoy/Istio's structured
    /// XFCC format — it deliberately does not).
    /// </summary>
    private static bool TryDecodeCertificate(string headerValue, out X509Certificate2? certificate)
    {
        certificate = null;

        if (string.IsNullOrWhiteSpace(headerValue))
        {
            return false;
        }

        try
        {
            var derBytes = Convert.FromBase64String(headerValue);
            certificate = X509CertificateLoader.LoadCertificate(derBytes);
            return true;
        }
        catch (FormatException)
        {
            // Not valid Base64 at all — fall through to the PEM path.
        }
        catch (CryptographicException)
        {
            // Valid Base64, but not a valid DER certificate — fall through to the PEM path.
        }

        try
        {
            var pem = Uri.UnescapeDataString(headerValue);
            certificate = X509Certificate2.CreateFromPem(pem);
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
