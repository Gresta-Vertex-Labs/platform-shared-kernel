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
    /// When <c>null</c>, default outbox options apply (PostgreSQL, 100 batch, 1 s delay, 30 min dedup window).
    /// Set <see cref="OutboxOptions.Database"/> when the <typeparamref name="TDbContext"/> is not on PostgreSQL.
    /// </param>
    /// <returns>The builder for fluent chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="OutboxOptions.Database"/> is not a defined value.</exception>
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

        // MassTransit locks outbox rows with SQL Server syntax unless told otherwise, which fails on every
        // delivery poll against PostgreSQL. Reject an undefined value here rather than at the first poll.
        Action<IEntityFrameworkOutboxConfigurator> useDatabase = opts.Database switch
        {
            OutboxDatabase.PostgreSql => o => o.UsePostgres(),
            OutboxDatabase.SqlServer => o => o.UseSqlServer(),
            OutboxDatabase.MySql => o => o.UseMySql(),
            OutboxDatabase.Sqlite => o => o.UseSqlite(),
            _ => throw new ArgumentOutOfRangeException(
                nameof(configure),
                opts.Database,
                $"{nameof(OutboxOptions)}.{nameof(OutboxOptions.Database)} is not a defined {nameof(OutboxDatabase)} value."),
        };

        return builder.ConfigureMassTransit(cfg =>
            cfg.AddEntityFrameworkOutbox<TDbContext>(o =>
            {
                useDatabase(o);
                o.QueryDelay = opts.QueryDelay;
                o.DuplicateDetectionWindow = opts.DuplicateDetectionWindow;
                o.UseBusOutbox(bo => bo.MessageDeliveryLimit = opts.BatchSize);
            }));
    }
}
