namespace SharedKernel.Messaging.MassTransit.Options;

/// <summary>
/// Configuration options for the RabbitMQ transport.
/// Bound from the <c>"SharedKernel:Messaging:RabbitMq"</c> configuration section.
/// </summary>
/// <remarks>
/// <para>
/// Never embed credentials in <c>appsettings.json</c> files committed to source control.
/// Source <see cref="Username"/> and <see cref="Password"/> from environment variables,
/// Kubernetes Secrets, or Azure Key Vault at runtime.
/// </para>
/// <para>
/// Prefetch tuning: lower values for slow, I/O-bound consumers; higher values for fast, CPU-bound consumers.
/// </para>
/// </remarks>
public sealed class RabbitMqBusOptions
{
    /// <summary>The configuration section key for <see cref="RabbitMqBusOptions"/>.</summary>
    public const string SectionName = "SharedKernel:Messaging:RabbitMq";

    /// <summary>
    /// Gets or sets the AMQP host URI.
    /// Examples: <c>"rabbitmq://localhost"</c>, <c>"amqps://rabbitmq.svc.cluster.local/vhost"</c>.
    /// </summary>
    public string Host { get; set; } = "rabbitmq://localhost";

    /// <summary>
    /// Gets or sets the RabbitMQ username.
    /// Default is <c>"guest"</c> — always override in non-local environments.
    /// </summary>
    public string Username { get; set; } = "guest";

    /// <summary>
    /// Gets or sets the RabbitMQ password.
    /// Default is <c>"guest"</c> — always override in non-local environments.
    /// </summary>
    public string Password { get; set; } = "guest";

    /// <summary>
    /// Gets or sets the RabbitMQ virtual host.
    /// Default is <c>"/"</c>.
    /// </summary>
    public string VirtualHost { get; set; } = "/";

    /// <summary>
    /// Gets or sets the number of messages pre-fetched per consumer channel.
    /// Default is <c>16</c>.
    /// Lower for slow consumers, higher for fast CPU-bound consumers.
    /// </summary>
    public ushort Prefetch { get; set; } = 16;

    /// <summary>
    /// Gets or sets the AMQP heartbeat interval used to detect stale connections.
    /// Default is <c>60 seconds</c>.
    /// </summary>
    public TimeSpan RequestedHeartbeat { get; set; } = TimeSpan.FromSeconds(60);
}
