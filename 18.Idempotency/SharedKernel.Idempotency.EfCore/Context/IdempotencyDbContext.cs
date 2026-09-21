using Microsoft.EntityFrameworkCore;
using SharedKernel.Idempotency.EfCore.Entities;

namespace SharedKernel.Idempotency.EfCore.Context;

/// <summary>
/// The minimal EF Core <see cref="DbContext"/> owning the two idempotency tables.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately does NOT extend <c>SharedKernel.Persistence.EfCore.Context.SharedKernelDbContext</c>
/// (D-06). That base always runs the platform save pipeline (audit stamping, soft delete, concurrency
/// translation, tenant guard). Its soft-delete step would silently rewrite the
/// documented cleanup recipe's hard <c>DELETE FROM ... WHERE expires_at_utc &lt; now()</c> into an
/// update, defeating retention entirely (Domain Invariant 5); its xmin concurrency convention
/// assumes a row-version column this table has no reason to carry. This context is plain EF Core,
/// no SharedKernel base, owning exactly two entities.
/// </para>
/// <para>
/// Configure the connection via <c>SharedKernel.Persistence.EfCore</c>'s
/// <c>DbContextOptionsBuilder.UsePostgres(dataSource)</c> extension (an <c>NpgsqlDataSource</c>) inside the
/// <c>configureDbContext</c> delegate passed to <c>AddSharedKernelEfCoreIdempotency</c> — this
/// gets snake_case table/column naming conventions and pgvector support (unused here, harmless)
/// for free, with no manual <c>ConfigureConventions</c> override required.
/// </para>
/// <para>
/// A consuming service that wants EF Core migrations for these two tables adds its own design-time
/// <c>IDesignTimeDbContextFactory&lt;IdempotencyDbContext&gt;</c> and a migrations project
/// referencing this package — see this package's <c>README.md</c> for the recipe. This package
/// ships no migrations of its own.
/// </para>
/// </remarks>
public sealed class IdempotencyDbContext(DbContextOptions<IdempotencyDbContext> options) : DbContext(options)
{
    /// <summary>The idempotency-key-store table.</summary>
    internal DbSet<IdempotencyKeyRecord> IdempotencyKeys => Set<IdempotencyKeyRecord>();

    /// <summary>The idempotency-message-store table.</summary>
    internal DbSet<IdempotencyMessageRecord> IdempotencyMessages => Set<IdempotencyMessageRecord>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfiguration(new IdempotencyKeyRecordConfiguration());
        modelBuilder.ApplyConfiguration(new IdempotencyMessageRecordConfiguration());
    }
}
