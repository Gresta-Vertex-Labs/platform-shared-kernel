namespace SharedKernel.Messaging.MassTransit.Options;

/// <summary>
/// Configuration options for the MassTransit EF Core transactional outbox delivery worker.
/// </summary>
/// <remarks>
/// <para>
/// These options configure MassTransit's built-in EF Core outbox delivery background service
/// that polls for unsent outbox rows and publishes them to the broker.
/// </para>
/// <para>
/// Delivery is <strong>at-least-once</strong> — all consumers must be idempotent.
/// The <see cref="DuplicateDetectionWindow"/> governs MassTransit's internal deduplication window.
/// </para>
/// <para>
/// The consuming service owns and runs the EF migrations for outbox tables.
/// Run <c>dotnet ef migrations add AddMassTransitOutbox</c> after calling
/// <c>.WithEntityFrameworkOutbox&lt;TDbContext&gt;()</c>.
/// </para>
/// </remarks>
public sealed class OutboxOptions
{
    /// <summary>
    /// Gets or sets the number of outbox rows fetched per delivery cycle.
    /// Default is <c>100</c>.
    /// </summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// Gets or sets the polling interval for new outbox messages.
    /// Default is <c>1 second</c>.
    /// </summary>
    public TimeSpan QueryDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets or sets the MassTransit deduplication window for at-least-once delivery.
    /// Messages with the same <c>MessageId</c> published within this window are discarded by consumers.
    /// Default is <c>30 minutes</c>.
    /// </summary>
    public TimeSpan DuplicateDetectionWindow { get; set; } = TimeSpan.FromMinutes(30);
}
