using Microsoft.EntityFrameworkCore;

namespace SharedKernel.Persistence.EfCore.Seeding;

/// <summary>
/// A startup data seeder for <typeparamref name="TContext"/>.
/// </summary>
/// <typeparam name="TContext">The <see cref="DbContext"/> type to seed.</typeparam>
/// <remarks>
/// <para>
/// Registered via <c>EfCorePersistenceBuilder&lt;TContext&gt;.AddSeeder&lt;TSeeder&gt;()</c>. Each
/// registered seeder is executed in registration order by
/// <c>MigrationAndSeedHostedService&lt;TContext&gt;</c> during host startup, each in its own DI
/// scope with its own <typeparamref name="TContext"/> instance resolved via
/// <c>IDbContextFactory&lt;TContext&gt;</c>.
/// </para>
/// <para>
/// <strong>Idempotency contract:</strong> idempotency is a CONTRACT, not enforced by the framework.
/// Implementations must check for existing data (or use upsert semantics) before inserting, since
/// <see cref="SeedAsync"/> may run on every application startup, and may run concurrently across
/// multiple replicas before the advisory lock is acquired by only one of them.
/// </para>
/// </remarks>
public interface IDataSeeder<TContext>
    where TContext : DbContext
{
    /// <summary>
    /// Seeds <paramref name="context"/> with the data owned by this seeder.
    /// </summary>
    /// <param name="context">A dedicated <typeparamref name="TContext"/> instance for this seeder.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when seeding has finished.</returns>
    Task SeedAsync(TContext context, CancellationToken ct = default);
}
