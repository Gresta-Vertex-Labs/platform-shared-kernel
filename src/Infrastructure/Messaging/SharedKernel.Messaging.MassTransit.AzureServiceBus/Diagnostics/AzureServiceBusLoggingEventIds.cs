using SharedKernel.Primitives.Logging;

namespace SharedKernel.Messaging.MassTransit.AzureServiceBus.Diagnostics;

/// <summary>
/// The EventIds of this package: sub-block 2 of the <see cref="LoggingEventIdRanges.Messaging"/> block (7200-7299).
/// </summary>
internal static class AzureServiceBusLoggingEventIds
{
    /// <summary>The first id of this package's sub-block.</summary>
    internal const int Base = LoggingEventIdRanges.Messaging + 2 * LoggingEventIdRanges.PackageSubBlockWidth;

    /// <summary><c>WithDeadLetterPolicy()</c> was called under the Azure Service Bus transport, where it has no effect.</summary>
    internal const int DeadLetterPolicyIgnored = Base + 0;
}
