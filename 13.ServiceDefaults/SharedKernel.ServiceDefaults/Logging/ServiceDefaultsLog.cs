using Microsoft.Extensions.Logging;

namespace SharedKernel.ServiceDefaults.Logging;

/// <summary>
/// Structured, source-generated <c>[LoggerMessage]</c> log events for
/// <c>SharedKernel.ServiceDefaults</c>.
/// </summary>
/// <remarks>
/// <para>
/// This domain's first-ever production logging (WO-061/P-395). Reserves <c>EventId</c> range
/// <c>13000</c>–<c>13099</c> within <c>01.Core</c>'s platform-wide <c>13000</c>–<c>13999</c>
/// domain block (the sibling package, <c>SharedKernel.MultiTenancy</c>, owns
/// <c>13100</c>–<c>13199</c> via <c>MultiTenancy.Logging.MultiTenancyLog</c>).
/// </para>
/// <para>
/// <b>Never logs a certificate's raw PEM/DER bytes, a raw JWT, or a raw header value verbatim</b> —
/// only thumbprint/subject/health-check-name-shaped identifiers and reason strings.
/// CorrelationId/TraceId/TenantId flow ambiently through the existing OTel logging pipeline
/// (<see cref="Telemetry.BaggageLogRecordProcessor"/>), never as an explicit template placeholder
/// here.
/// </para>
/// </remarks>
internal static partial class ServiceDefaultsLog
{
    /// <summary>
    /// Logged when a mutual-TLS client certificate (negotiated directly by Kestrel, or forwarded via
    /// <see cref="Security.MtlsForwardedHeaderMiddleware"/>) is accepted by the registered
    /// <c>IMtlsCertificateValidator</c>.
    /// </summary>
    [LoggerMessage(
        EventId = 13000,
        Level = LogLevel.Information,
        Message = "Mutual-TLS client certificate accepted (thumbprint: {Thumbprint}, subject: {Subject}).")]
    public static partial void MtlsCertificateAccepted(ILogger logger, string thumbprint, string subject);

    /// <summary>
    /// Logged when a mutual-TLS client certificate is rejected — either by the registered
    /// <c>IMtlsCertificateValidator</c>, or by <see cref="Security.MtlsForwardedHeaderMiddleware"/>'s
    /// <c>TrustedNetworks</c> allowlist before the certificate was even decoded (in which case
    /// <paramref name="thumbprint"/>/<paramref name="subject"/> are placeholders — see
    /// <paramref name="reason"/> for the actual cause).
    /// </summary>
    [LoggerMessage(
        EventId = 13001,
        Level = LogLevel.Warning,
        Message = "Mutual-TLS client certificate rejected (thumbprint: {Thumbprint}, subject: {Subject}, reason: {Reason}).")]
    public static partial void MtlsCertificateRejected(ILogger logger, string thumbprint, string subject, string reason);

    /// <summary>
    /// Logged once per dependency-specific health check registered via an
    /// <c>Add*Check</c>/<c>Add*ReadinessCheck</c> extension method — never once per probe
    /// invocation.
    /// </summary>
    [LoggerMessage(
        EventId = 13002,
        Level = LogLevel.Information,
        Message = "Health check registered (name: {HealthCheckName}, tags: {Tags}).")]
    public static partial void HealthCheckRegistered(ILogger logger, string healthCheckName, string tags);

    /// <summary>
    /// Logged once at startup when <see cref="Security.MtlsForwardedHeaderExtensions.AddMtlsForwardedHeaderCertificate"/>
    /// is configured with an empty <c>TrustedNetworks</c> allowlist — any network path reaching this
    /// host directly (a misconfigured <c>NetworkPolicy</c>, a multi-hop mesh topology, a debug port,
    /// a compromised sidecar) can forge the forwarded-certificate header identically to the real
    /// ingress.
    /// </summary>
    [LoggerMessage(
        EventId = 13003,
        Level = LogLevel.Warning,
        Message = "MtlsForwardedHeaderMiddleware has no configured TrustedNetworks allowlist for header '{HeaderName}' — any network path reaching this host directly can forge this header.")]
    public static partial void ForwardedHeaderTrustBoundaryUnconfigured(ILogger logger, string headerName);

    /// <summary>
    /// Logged once at startup by <see cref="Localization.LocalizationExtensions.AddSharedKernelLocalization"/>
    /// when neither the <c>UserPreference</c> nor the <c>TenantDefault</c> culture-resolution step
    /// can ever resolve a culture — <c>LocalizationResolutionOptions.UserPreferenceClaimType</c> is
    /// unconfigured AND no <c>ITenantCatalog</c> is registered in DI.
    /// </summary>
    [LoggerMessage(
        EventId = 13004,
        Level = LogLevel.Warning,
        Message = "AddSharedKernelLocalization: neither UserPreferenceClaimType nor a registered ITenantCatalog is configured — the UserPreference and TenantDefault culture-resolution steps can never resolve a culture.")]
    public static partial void LocalizationNoDynamicStrategyCanResolve(ILogger logger);
}
