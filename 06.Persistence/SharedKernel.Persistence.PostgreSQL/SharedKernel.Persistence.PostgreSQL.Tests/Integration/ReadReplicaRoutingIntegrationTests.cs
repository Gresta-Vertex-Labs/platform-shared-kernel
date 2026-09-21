using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using SharedKernel.Domain.Specifications;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Extensions;
using SharedKernel.Persistence.EfCore.Interceptors;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Persistence;
using SharedKernel.Persistence.EfCore.Options;
using SharedKernel.Persistence.EfCore.ReadReplica;
using SharedKernel.Persistence.EfCore.Repositories;
using SharedKernel.Persistence.EfCore.Specifications;
using SharedKernel.Persistence.PostgreSQL.Extensions;
using SharedKernel.Primitives.Clocks;
using Testcontainers.PostgreSql;

namespace SharedKernel.Persistence.PostgreSQL.Tests.Integration;

/// <summary>
/// Starts TWO independent, real PostgreSQL containers acting as primary/replica stand-ins, each
/// seeded with a distinguishable marker row, for <see cref="ReadReplicaRoutingIntegrationTests"/>.
/// Deliberately NOT the shared single-container
/// <see cref="SharedKernel.Testing.Containers.PostgreSqlContainerFixture"/>/<see cref="PostgreSqlTestCollection"/>
/// fixture — this proof specifically requires two DISTINCT database servers to demonstrate routing
/// occurs at the connection level, not merely the API-surface level.
/// </summary>
public sealed class ReadReplicaTwoContainerFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _primaryContainer = new PostgreSqlBuilder("postgres:16.4")
        .WithDatabase("sk_replica_primary")
        .WithUsername("sharedkernel")
        .WithPassword("sharedkernel")
        .Build();

    private readonly PostgreSqlContainer _replicaContainer = new PostgreSqlBuilder("postgres:16.4")
        .WithDatabase("sk_replica_replica")
        .WithUsername("sharedkernel")
        .WithPassword("sharedkernel")
        .Build();

    /// <summary>Connection string for the "primary" stand-in container.</summary>
    public string PrimaryConnectionString => _primaryContainer.GetConnectionString();

    /// <summary>Connection string for the "replica" stand-in container.</summary>
    public string ReplicaConnectionString => _replicaContainer.GetConnectionString();

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        await Task.WhenAll(_primaryContainer.StartAsync(), _replicaContainer.StartAsync());

        // Seed each container with a DIFFERENT marker row, exactly once for the whole fixture
        // lifetime (shared across every [Fact] in the test class via IClassFixture<T>).
        await SeedMarkerRowAsync(PrimaryConnectionString, "PrimaryOnly");
        await SeedMarkerRowAsync(ReplicaConnectionString, "ReplicaOnly");
    }

    private static async Task SeedMarkerRowAsync(string connectionString, string markerName)
    {
        var builder = new DbContextOptionsBuilder<ConcurrencyTestDbContext>();
        builder.UsePostgreSQL(connectionString);
        var options = builder.Options;

        var actorContext = new FakeAuditActorContext();
        var clock = new FakeClock();

        var audit = new AuditInterceptor(actorContext, clock);
        var softDelete = new SoftDeleteInterceptor(clock);
        var concurrency = new ConcurrencyInterceptor();

        await using var ctx = new ConcurrencyTestDbContext(options, new PersistenceContextDependencies(audit, softDelete, concurrency));
        await ctx.Database.EnsureCreatedAsync();
        ctx.Aggregates.Add(new ConcurrentPgAggregate(ConcurrentPgId.New(), markerName, new SystemClock()));
        await ctx.SaveChangesAsync();
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        await _primaryContainer.DisposeAsync();
        await _replicaContainer.DisposeAsync();
    }
}

// A minimal, directly-constructible EfReadRepository subclass over the shared
// ConcurrencyTestDbContext/ConcurrentPgAggregate fixtures (ConcurrencyIntegrationTests.cs), taking
// the optional read-replica accessor exactly like any real consumer would.
internal sealed class ConcurrentPgAggregateReadRepository(
    ConcurrencyTestDbContext ctx,
    IReadReplicaContextAccessor<SharedKernelDbContext>? accessor)
        : EfReadRepository<ConcurrentPgAggregate, ConcurrentPgId>(
        ctx, new SpecificationEvaluator<ConcurrentPgAggregate>(), accessor);

/// <summary>
/// T-114/T-116: read-replica routing proof against two
/// independent real PostgreSQL containers — proves routing genuinely occurs at the connection
/// level (a read observes a DIFFERENT database server's data), and that a read inside an active
/// primary transaction never diverges from the primary.
/// </summary>
public sealed class ReadReplicaRoutingIntegrationTests : IClassFixture<ReadReplicaTwoContainerFixture>
{
    private readonly ReadReplicaTwoContainerFixture _fixture;

    public ReadReplicaRoutingIntegrationTests(ReadReplicaTwoContainerFixture fixture)
    {
        _fixture = fixture;
    }

    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services
            .AddSharedKernelEfCore<ConcurrencyTestDbContext>(opts => opts.UsePostgreSQL(_fixture.PrimaryConnectionString))
            .WithReadReplica(opts => opts.UsePostgreSQL(_fixture.ReplicaConnectionString))
            .Build();

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task ReadRepository_NoActiveTransaction_ObservesReplicaContainerState_NeverPrimarys()
    {
        // Arrange
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<ConcurrencyTestDbContext>();
        var accessor = scope.ServiceProvider.GetRequiredService<IReadReplicaContextAccessor<SharedKernelDbContext>>();
        var repo = new ConcurrentPgAggregateReadRepository(ctx, accessor);

        // Act
        var results = await repo.ListAsync(new AllSpecification<ConcurrentPgAggregate>());

        // Assert — the read observed the REPLICA container's seeded state, proving routing
        // genuinely occurs at the connection level, not merely the API-surface level.
        results.Should().Contain(a => a.Name == "ReplicaOnly");
        results.Should().NotContain(a => a.Name == "PrimaryOnly");
    }

    [Fact]
    public async Task ReadRepository_ActiveTransactionOnPrimary_ObservesPrimaryContainerState_NeverReplicas()
    {
        // Arrange
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<ConcurrencyTestDbContext>();
        var accessor = scope.ServiceProvider.GetRequiredService<IReadReplicaContextAccessor<SharedKernelDbContext>>();
        var repo = new ConcurrentPgAggregateReadRepository(ctx, accessor);

        // Act — an explicit EF Core transaction is active on the primary.
        await using var transaction = await ctx.Database.BeginTransactionAsync();
        var results = await repo.ListAsync(new AllSpecification<ConcurrentPgAggregate>());
        await transaction.RollbackAsync();

        // Assert — routing must NEVER divert to the replica while a transaction is open on the
        // primary, even though replica routing is otherwise enabled and reachable.
        results.Should().Contain(a => a.Name == "PrimaryOnly");
        results.Should().NotContain(a => a.Name == "ReplicaOnly");
    }
}
