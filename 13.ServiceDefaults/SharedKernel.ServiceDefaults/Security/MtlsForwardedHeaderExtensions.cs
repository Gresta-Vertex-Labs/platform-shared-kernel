using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharedKernel.ServiceDefaults.Logging;
using SharedKernel.Security.Mtls.Validation;

namespace SharedKernel.ServiceDefaults.Security;

/// <summary>
/// DI registration for <see cref="MtlsForwardedHeaderMiddleware"/>.
/// </summary>
public static class MtlsForwardedHeaderExtensions
{
    /// <summary>
    /// Registers <see cref="MtlsForwardedHeaderOptions"/> — validated so an unconfigured
    /// <see cref="MtlsForwardedHeaderOptions.HeaderName"/> fails fast at startup — and
    /// <see cref="MtlsForwardedHeaderMiddleware"/> in DI, for hosts where TLS terminates at an
    /// ingress/gateway ahead of Kestrel.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <param name="configure">
    /// Configuration delegate for <see cref="MtlsForwardedHeaderOptions"/>. Required — there is no
    /// safe default <see cref="MtlsForwardedHeaderOptions.HeaderName"/> (see its own remarks for
    /// why).
    /// </param>
    /// <returns>The same <paramref name="builder"/> instance, for fluent chaining.</returns>
    /// <remarks>
    /// <para>
    /// Does not register <see cref="MtlsForwardedHeaderMiddleware"/> into the request pipeline
    /// itself — that remains an explicit
    /// <c>app.UseMiddleware&lt;MtlsForwardedHeaderMiddleware&gt;()</c> call by the consumer,
    /// mirroring <c>SharedKernel.MultiTenancy.AddSharedKernelMultiTenancy</c>'s "register services
    /// here, wire the middleware separately" split — registering the services without wiring the
    /// middleware leaves it silently absent from the pipeline (a no-op, not a crash).
    /// </para>
    /// <para>
    /// This method also does not register an <see cref="IMtlsCertificateValidator"/> — that remains
    /// a separate call, typically <c>SharedKernel.Security.Mtls</c>'s
    /// <c>AddMtlsAuthentication&lt;TValidator&gt;()</c>, or a direct
    /// <c>services.AddScoped&lt;IMtlsCertificateValidator, TValidator&gt;()</c>. Omitting it leaves
    /// <see cref="MtlsForwardedHeaderMiddleware"/> unable to resolve its required dependency at
    /// first request.
    /// </para>
    /// <para>
    /// <b>Trust-boundary warning (WO-061/P-394):</b> when <paramref name="configure"/> leaves
    /// <see cref="MtlsForwardedHeaderOptions.TrustedNetworks"/> empty, a one-time startup
    /// <c>ServiceDefaultsLog.ForwardedHeaderTrustBoundaryUnconfigured</c> warning fires the first
    /// time <see cref="MtlsForwardedHeaderOptions"/> is resolved — forced to happen during host
    /// startup by the <c>.ValidateOnStart()</c> chain below, not deferred to first request.
    /// </para>
    /// </remarks>
    public static IHostApplicationBuilder AddMtlsForwardedHeaderCertificate(
        this IHostApplicationBuilder builder,
        Action<MtlsForwardedHeaderOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        builder.Services
            .AddOptions<MtlsForwardedHeaderOptions>()
            .Configure(configure)
            .PostConfigure<ILoggerFactory>((configuredOptions, loggerFactory) =>
            {
                if (configuredOptions.TrustedNetworks.Count == 0)
                {
                    var logger = loggerFactory.CreateLogger(
                        "SharedKernel.ServiceDefaults.Security.MtlsForwardedHeaderMiddleware");
                    ServiceDefaultsLog.ForwardedHeaderTrustBoundaryUnconfigured(logger, configuredOptions.HeaderName);
                }
            })
            .Validate(
                static options => !string.IsNullOrWhiteSpace(options.HeaderName),
                $"{nameof(MtlsForwardedHeaderOptions)}.{nameof(MtlsForwardedHeaderOptions.HeaderName)} must be configured explicitly — it carries no default tied to any one ingress/gateway vendor's convention.")
            .ValidateOnStart();

        return builder;
    }
}
