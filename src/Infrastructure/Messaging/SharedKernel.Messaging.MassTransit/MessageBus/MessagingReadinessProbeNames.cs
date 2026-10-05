namespace SharedKernel.Messaging.MassTransit.MessageBus;

/// <summary>The names of the readiness probes this package registers.</summary>
public static class MessagingReadinessProbeNames
{
    /// <summary>
    /// The probe of the configured message bus, registered by <c>MessagingBusBuilder.Build()</c>. Resolve it
    /// with <c>GetRequiredReadinessProbe(MessagingReadinessProbeNames.Bus)</c>.
    /// </summary>
    public const string Bus = "messaging";
}
