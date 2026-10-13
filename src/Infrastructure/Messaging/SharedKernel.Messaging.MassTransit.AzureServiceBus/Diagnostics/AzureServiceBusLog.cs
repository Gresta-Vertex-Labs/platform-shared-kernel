using Microsoft.Extensions.Logging;

namespace SharedKernel.Messaging.MassTransit.AzureServiceBus.Diagnostics;

/// <summary>
/// Source-generated <c>[LoggerMessage]</c> log statements of this package. The ids are
/// <see cref="AzureServiceBusLoggingEventIds"/> (7200-7299).
/// </summary>
internal static partial class AzureServiceBusLog
{
    /// <summary>
    /// <c>DeadLetterOptions</c> is RabbitMQ-only, so <c>WithDeadLetterPolicy()</c> is a no-op under Azure Service Bus,
    /// whose dead-lettering is driven by the queue or subscription <c>MaxDeliveryCount</c> set on the Azure resource.
    /// </summary>
    [LoggerMessage(
        EventId = AzureServiceBusLoggingEventIds.DeadLetterPolicyIgnored,
        Level = LogLevel.Warning,
        Message = "WithDeadLetterPolicy() has no effect under Azure Service Bus, whose dead-lettering is driven by the MaxDeliveryCount set on the Azure resource.")]
    internal static partial void DeadLetterPolicyIgnored(ILogger logger);
}
