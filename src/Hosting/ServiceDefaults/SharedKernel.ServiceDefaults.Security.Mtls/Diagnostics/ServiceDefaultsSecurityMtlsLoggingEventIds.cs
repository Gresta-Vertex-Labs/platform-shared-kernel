using SharedKernel.Primitives.Logging;

namespace SharedKernel.ServiceDefaults.Security.Mtls.Diagnostics;

/// <summary>
/// The EventIds of this package: sub-block 3 of the <see cref="LoggingEventIdRanges.ServiceDefaults"/> block
/// (13300-13399). Not the <c>SharedKernel.Security.Mtls</c> block, which lies in 12xxx (12300-12399).
/// </summary>
internal static class ServiceDefaultsSecurityMtlsLoggingEventIds
{
    /// <summary>The first id of this package's sub-block.</summary>
    internal const int Base = LoggingEventIdRanges.ServiceDefaults + 3 * LoggingEventIdRanges.PackageSubBlockWidth;

    /// <summary>The validator accepted a client certificate.</summary>
    internal const int CertificateAccepted = Base + 0;

    /// <summary>The validator rejected a client certificate.</summary>
    internal const int CertificateRejected = Base + 1;

    /// <summary>A forwarded client certificate was ignored because its source is outside the trusted networks.</summary>
    internal const int ForwardedCertificateRefused = Base + 2;

    /// <summary>The forwarded-header path has no trusted networks configured.</summary>
    internal const int ForwardedHeaderTrustNotConfigured = Base + 3;
}
