using SharedKernel.Primitives.Logging;

namespace SharedKernel.Communication.Grpc.Internal;

/// <summary>
/// The EventIds of this package: sub-block 1 of the <see cref="LoggingEventIdRanges.Communication"/> block
/// (11100-11199).
/// </summary>
internal static class CommunicationGrpcLoggingEventIds
{
    /// <summary>The first id of this package's sub-block.</summary>
    internal const int Base = LoggingEventIdRanges.Communication + 1 * LoggingEventIdRanges.PackageSubBlockWidth;

    /// <summary>The caller could not be written onto a call.</summary>
    internal const int PropagationFailed = Base + 0;
}
