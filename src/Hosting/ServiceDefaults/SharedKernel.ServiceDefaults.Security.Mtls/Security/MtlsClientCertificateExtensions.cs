using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharedKernel.ServiceDefaults.Logging;
using SharedKernel.Security.Mtls.Validation;

namespace SharedKernel.ServiceDefaults.Security;

/// <summary>
/// Composes mutual-TLS client-certificate acceptance directly into Kestrel, for hosts where TLS
/// terminates at Kestrel itself.
/// </summary>
/// <remarks>
/// For hosts where TLS instead terminates at an ingress/gateway ahead of Kestrel, which forwards
/// the client certificate as a request header, see
/// <see cref="MtlsForwardedHeaderExtensions.AddMtlsForwardedHeaderCertificate"/> instead. A host
/// MAY call both if its deployment topology genuinely varies by environment.
/// </remarks>
public static class MtlsClientCertificateExtensions
{
    /// <summary>
    /// Configures Kestrel to negotiate a mutual-TLS client certificate on every HTTPS connection,
    /// delegating the accept/reject decision to the registered <see cref="IMtlsCertificateValidator"/>.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <param name="mode">
    /// The client-certificate negotiation mode. Defaults to
    /// <see cref="ClientCertificateMode.AllowCertificate"/> (request, but do not require, a client
    /// certificate). Pass <see cref="ClientCertificateMode.RequireCertificate"/> for a host that
    /// must reject any connection presenting no client certificate.
    /// </param>
    /// <returns>The same <paramref name="builder"/> instance, for fluent chaining.</returns>
    /// <remarks>
    /// <para>
    /// <b>This method never reimplements X.509 chain validation, revocation checking, or
    /// subject/issuer matching.</b> It wires
    /// <see cref="HttpsConnectionAdapterOptions.ClientCertificateMode"/> on Kestrel and delegates
    /// the actual accept/reject decision entirely to whatever <see cref="IMtlsCertificateValidator"/>
    /// the consuming service has registered — typically via <c>SharedKernel.Security.Mtls</c>'s
    /// <c>AddMtlsAuthentication&lt;TValidator&gt;()</c>, or a direct
    /// <c>services.AddScoped&lt;IMtlsCertificateValidator, TValidator&gt;()</c> call. This method
    /// registers no validator of its own; certificate trust decisions stay in <c>SharedKernel.Security.Mtls</c>.
    /// </para>
    /// <para>
    /// <b>Blocking-bridge cost — documented deliberately, not a formality to wave away:</b>
    /// <see cref="IMtlsCertificateValidator.ValidateAsync"/> is async-only (confirmed by reading the
    /// shipped <c>SharedKernel.Security.Mtls</c> source directly, WO-058/P-377), but Kestrel's
    /// <see cref="HttpsConnectionAdapterOptions.ClientCertificateValidation"/> delegate is
    /// synchronous (<c>Func&lt;X509Certificate2, X509Chain?, SslPolicyErrors, bool&gt;</c>) and runs
    /// inside the TLS handshake itself, which cannot <c>await</c>. This method bridges the two with
    /// a blocking <c>.GetAwaiter().GetResult()</c> call. <b>THIS IS A REAL LATENCY AND
    /// THREAD-POOL-STARVATION COST UNDER LOAD</b> — every negotiated TLS handshake blocks a thread
    /// pool thread for the duration of <see cref="IMtlsCertificateValidator.ValidateAsync"/>. A
    /// validator registered for use with this method should resolve quickly (e.g. an in-memory
    /// allow-list or a locally cached trust decision) and must never perform a slow remote call (a
    /// network round trip to a CRL/OCSP responder or an external policy service) on this path.
    /// </para>
    /// <para>
    /// <b>Scoped-service resolution — corrected from the original provisional design:</b>
    /// <c>IMtlsCertificateValidator</c> is registered <c>Scoped</c> by
    /// <c>AddMtlsAuthentication&lt;TValidator&gt;()</c>. Kestrel's TLS handshake runs entirely
    /// outside any HTTP request scope — unlike <c>SharedKernel.Security.Mtls</c>'s own
    /// <c>CertificateAuthenticationHandler.OnCertificateValidated</c> event (which runs later,
    /// inside the ASP.NET Core authentication middleware pipeline, where
    /// <c>HttpContext.RequestServices</c> is available), there is no ambient request scope at the
    /// Kestrel connection level. Capturing <c>IMtlsCertificateValidator</c> itself via
    /// <c>Configure&lt;IMtlsCertificateValidator&gt;</c> would resolve it once, from the ROOT
    /// container, at Kestrel-options-configuration time — exactly the request-scoped-DI-resolution
    /// pitfall <c>src/Hosting/Security/CLAUDE.md</c> documents, and it throws under
    /// <c>ServiceProviderOptions.ValidateScopes = true</c> (the default in Development). This
    /// method instead captures <see cref="IServiceScopeFactory"/> (never itself Scoped) and creates
    /// a fresh <see cref="IServiceScope"/> per TLS handshake — the standard, safe pattern for
    /// resolving a Scoped service from outside any ambient request scope.
    /// </para>
    /// </remarks>
    public static IHostApplicationBuilder AddMtlsClientCertificate(
        this IHostApplicationBuilder builder,
        ClientCertificateMode mode = ClientCertificateMode.AllowCertificate)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services
            .AddOptions<KestrelServerOptions>()
            .Configure<IServiceScopeFactory>((kestrelOptions, scopeFactory) =>
            {
                kestrelOptions.ConfigureHttpsDefaults(https =>
                {
                    https.ClientCertificateMode = mode;
                    https.ClientCertificateValidation = (certificate, _, _) =>
                        ValidateCertificate(certificate, scopeFactory);
                });
            });

        return builder;
    }

    /// <summary>
    /// Bridges Kestrel's synchronous <c>ClientCertificateValidation</c> callback to the async-only
    /// <see cref="IMtlsCertificateValidator.ValidateAsync"/>, resolving the Scoped validator (and,
    /// for audit logging — WO-061/P-395 — the <see cref="ILogger"/>) from a freshly-created
    /// <see cref="IServiceScope"/>. See the "Blocking-bridge cost" and "Scoped-service resolution"
    /// remarks on <see cref="AddMtlsClientCertificate"/> for why both of these are necessary here
    /// and nowhere else in this call chain.
    /// </summary>
    private static bool ValidateCertificate(X509Certificate2 certificate, IServiceScopeFactory scopeFactory)
    {
        using var scope = scopeFactory.CreateScope();
        var validator = scope.ServiceProvider.GetRequiredService<IMtlsCertificateValidator>();

        var result = validator.ValidateAsync(certificate, CancellationToken.None).GetAwaiter().GetResult();

        var logger = scope.ServiceProvider
            .GetService<ILoggerFactory>()?
            .CreateLogger("SharedKernel.ServiceDefaults.Security.MtlsClientCertificateExtensions");

        if (logger is not null)
        {
            if (result.IsValid)
            {
                MtlsLog.MtlsCertificateAccepted(logger, certificate.Thumbprint, certificate.Subject);
            }
            else
            {
                MtlsLog.MtlsCertificateRejected(
                    logger,
                    certificate.Thumbprint,
                    certificate.Subject,
                    "rejected by IMtlsCertificateValidator");
            }
        }

        return result.IsValid;
    }
}
