using Microsoft.EntityFrameworkCore;
using SharedKernel.Idempotency.EfCore.Entities;

namespace SharedKernel.Idempotency.EfCore.Context;

/// <summary>The minimal EF Core <see cref="DbContext"/> owning the idempotency table.</summary>
/// <remarks>
/// <para>
/// Deliberately does NOT extend <c>SharedKernel.Persistence.EfCore.Context.SharedKernelDbContext</c>. That base runs
/// the platform save pipeline (audit stamping, soft delete, concurrency translation, tenant guard): its soft-delete
/// step would turn the documented cleanup job's hard <c>DELETE … WHERE expires_at_utc &lt; now()</c> into an update,
/// and its <c>xmin</c> convention assumes a row version this table has no use for.
/// </para>
/// <para>
/// Configure the connection with <c>SharedKernel.Persistence.EfCore</c>'s <c>UsePostgres(dataSource)</c> inside the
/// <c>configureDbContext</c> delegate passed to <c>AddEfCoreIdempotency</c>, which also applies snake_case naming.
/// This package ships no migrations; see <c>README.md</c> for the design-time factory recipe.
/// </para>
/// </remarks>
public sealed class IdempotencyDbContext(DbContextOptions<IdempotencyDbContext> options) : DbContext(options)
{
    /// <summary>The idempotency table, one row per (tenant scope, purpose, key).</summary>
    internal DbSet<IdempotencyKeyRecord> IdempotencyKeys => Set<IdempotencyKeyRecord>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfiguration(new IdempotencyKeyRecordConfiguration());
    }
}
