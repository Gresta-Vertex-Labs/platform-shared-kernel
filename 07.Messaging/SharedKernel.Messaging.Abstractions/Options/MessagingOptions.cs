namespace SharedKernel.Messaging.Abstractions.Options;

/// <summary>
/// The messaging layer's configuration, bound from <c>SharedKernel:Messaging</c>.
/// </summary>
/// <remarks>
/// <para>
/// One setting, because one setting is genuinely all this layer needs from a host that is not
/// choosing a transport. Everything else — connection strings, retry shape, dead-letter TTL,
/// payload transform — belongs to the builder call that turns it on.
/// </para>
/// <para>
/// Bind it with <c>services.AddSharedKernelMessaging(configuration)</c>, which reads
/// <see cref="SectionName"/> itself, so no call site names the section.
/// </para>
/// </remarks>
/// <example>
/// <code language="json">
/// {
///   "SharedKernel": { "Messaging": { "ServiceName": "order-service" } }
/// }
/// </code>
/// </example>
public sealed class MessagingOptions
{
    /// <summary>The configuration section this type binds from.</summary>
    public const string SectionName = "SharedKernel:Messaging";

    /// <summary>
    /// This service's name on the bus: a lowercase slug such as <c>order-service</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Not a label — two wire contracts at once.</strong> It is the prefix of every queue
    /// and exchange this service declares (<c>order-service-order-placed</c> for an
    /// <c>OrderPlacedConsumer</c>), and it is the CloudEvents <c>source</c> on every integration
    /// event this service publishes, which subscribers filter on. Changing it after deployment
    /// orphans the old queues and breaks any filter that matched the old source.
    /// </para>
    /// <para>
    /// <strong>Validated at startup</strong> as <c>^[a-z0-9]+(-[a-z0-9]+)*$</c>, at most 100
    /// characters. The rule is stricter than any single broker requires — RabbitMQ would accept a
    /// dot, Azure Service Bus a slash — because a name legal on one transport and not the other
    /// turns a transport switch into a rename of every queue in the deployment. A capital or a
    /// space would otherwise produce a queue name a broker either rejects or quietly mangles, far
    /// from the configuration that caused it (P-561).
    /// </para>
    /// <para>
    /// Give each deployable service its own name. Two services sharing one name share their
    /// queues, and each will consume messages meant for the other.
    /// </para>
    /// </remarks>
    public string ServiceName { get; set; } = string.Empty;
}
