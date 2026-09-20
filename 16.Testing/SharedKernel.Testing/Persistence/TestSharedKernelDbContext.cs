using Microsoft.EntityFrameworkCore;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Interceptors;
using SharedKernel.Testing.Clocks;

namespace SharedKernel.Testing.Persistence;

/// <summary>
/// Abstract <see cref="SharedKernelDbContext"/> subclass preconfigured with the SQLite
/// in-memory provider for fast, isolated EF Core tests.
/// </summary>
/// <remarks>
/// <para>
/// P-557/W2: wires a fixed <see cref="ICurrentActorContext"/> (<c>Persistence/FakeAuditActorContext</c>,
/// actor id <c>"test-user"</c>) so <c>AuditInterceptor</c>/<c>SoftDeleteInterceptor</c> resolve without
/// a real security bridge, and a deterministic <see cref="FakeClock"/> (fixed snapshot, never real
/// time) for stable interceptor timestamps. This class no longer references
/// <c>SharedKernel.Security.Abstractions</c> at all — the platform interceptors were retargeted onto
/// the local <see cref="ICurrentActorContext"/> seam in <c>06.Persistence</c>.
/// </para>
/// <para>Enables <see cref="DbContextOptionsBuilder.EnableSensitiveDataLogging"/> for readable test diagnostics.</para>
/// </remarks>
public abstract class TestSharedKernelDbContext : SharedKernelDbContext
{
    /// <summary>
    /// Initialises a new <see cref="TestSharedKernelDbContext"/> over the SQLite in-memory provider.
    /// </summary>
    /// <param name="options">EF Core context options — typically built via <see cref="BuildOptions"/>.</param>
    protected TestSharedKernelDbContext(DbContextOptions options)
        : base(
            options,
            new PersistenceContextDependencies(
                new AuditInterceptor(TestActor, new FakeClock()),
                new SoftDeleteInterceptor(TestActor, new FakeClock()),
                new ConcurrencyInterceptor()))
    {
        // EF Core's internal per-context service provider does not expose the constructor-supplied
        // DbContextOptions as a resolvable service for standalone (non-DI-hosted) contexts, so
        // EfContextExtensions.ReloadAsync cannot recover them via GetService<T>(). Registering here
        // means every TestSharedKernelDbContext subclass supports ReloadAsync with zero extra setup.
        EfContextExtensions.RegisterOptions(this, options);
    }

    /// <summary>
    /// Builds <see cref="DbContextOptions{TContext}"/> for a SQLite in-memory database identified
    /// by <paramref name="databaseName"/>, with sensitive data logging enabled.
    /// </summary>
    /// <typeparam name="TContext">The concrete context type.</typeparam>
    /// <param name="databaseName">
    /// The shared in-memory database name. Use a unique value per test to avoid cross-test
    /// state leakage, or a shared value within a single test to simulate multiple scoped contexts
    /// against the same database.
    /// </param>
    /// <returns>The configured <see cref="DbContextOptions{TContext}"/>.</returns>
    public static DbContextOptions<TContext> BuildOptions<TContext>(string databaseName)
        where TContext : DbContext
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);

        return new DbContextOptionsBuilder<TContext>()
            .UseSqlite($"DataSource=file:{databaseName}?mode=memory&cache=shared")
            .EnableSensitiveDataLogging()
            .Options;
    }

    /// <summary>
    /// Ensures the underlying SQLite in-memory database schema is created. No migrations are
    /// needed for SQLite tests.
    /// </summary>
    public Task EnsureCreatedAsync() => Database.EnsureCreatedAsync();

    // The fixed actor the audit interceptors record.
    private static readonly ICurrentActorContext TestActor = new FakeAuditActorContext("test-user");
}
