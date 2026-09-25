using MassTransit;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Messaging.MassTransit.Options;

namespace SharedKernel.Messaging.MassTransit.Extensions;

/// <summary>
/// Adds MassTransit's EF Core transactional outbox to the messaging bus.
/// </summary>
/// <remarks>
/// Declared in the builder's own namespace, so <c>AddSharedKernelMessaging(...).WithEntityFrameworkOutbox&lt;T&gt;()</c>
/// needs no extra <c>using</c>. It lives in its own package so that a service without an outbox does not reference
/// EF Core (P-570).
/// </remarks>
public static class EntityFrameworkOutboxMessagingBusBuilderExtensions
{
    /// <summary>
    /// Wires the MassTransit EF Core transactional outbox using the consuming service's
    /// <typeparamref name="TDbContext"/>. Call it once.
    /// </summary>
    /// <typeparam name="TDbContext">
    /// The consuming service's EF Core <see cref="DbContext"/> that includes MassTransit outbox tables.
    /// </typeparam>
    /// <param name="builder">The messaging bus builder.</param>
    /// <param name="configure">
    /// Optional action to customise <see cref="OutboxOptions"/>.
    /// When <c>null</c>, default outbox options apply (100 batch, 1 s delay, 30 min dedup window).
    /// </param>
    /// <returns>The builder for fluent chaining.</returns>
    /// <remarks>
    /// <para>
    /// The consuming service's <typeparamref name="TDbContext"/> must include the MassTransit outbox
    /// tables. Run <c>dotnet ef migrations add AddMassTransitOutbox</c> after calling this method.
    /// </para>
    /// <para>
    /// This package provides no migrations — the consuming service owns and runs them.
    /// </para>
    /// <para>
    /// At-least-once delivery is guaranteed; all consumers must be idempotent.
    /// </para>
    /// </remarks>
    public static MessagingBusBuilder WithEntityFrameworkOutbox<TDbContext>(
        this MessagingBusBuilder builder,
        Action<OutboxOptions>? configure = null)
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(builder);

        var opts = new OutboxOptions();
        configure?.Invoke(opts);

        return builder.ConfigureMassTransit(cfg =>
            cfg.AddEntityFrameworkOutbox<TDbContext>(o =>
            {
                o.QueryDelay = opts.QueryDelay;
                o.DuplicateDetectionWindow = opts.DuplicateDetectionWindow;
                o.UseBusOutbox(bo => bo.MessageDeliveryLimit = opts.BatchSize);
            }));
    }
}
