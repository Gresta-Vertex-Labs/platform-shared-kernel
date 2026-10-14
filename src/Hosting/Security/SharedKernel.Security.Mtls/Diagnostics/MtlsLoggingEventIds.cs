using SharedKernel.Primitives.Logging;

namespace SharedKernel.Security.Mtls.Diagnostics;

/// <summary>
/// The EventIds of this package: sub-block 3 of the <see cref="LoggingEventIdRanges.Security"/> block (12300-12399).
/// Not the <c>ServiceDefaults.Security.Mtls</c> block, which lies in 13xxx.
/// </summary>
internal static class MtlsLoggingEventIds
{
    /// <summary>The first id of this package's sub-block.</summary>
    internal const int Base = LoggingEventIdRanges.Security + 3 * LoggingEventIdRanges.PackageSubBlockWidth;

    /// <summary>The validator rejected the client certificate.</summary>
    internal const int CertificateRejected = Base + 0;

    /// <summary>The validator threw and the client certificate was rejected.</summary>
    internal const int ValidatorFailed = Base + 1;
}
