using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Extensions;
using SharedKernel.Persistence.EfCore.ReadReplica;
using SharedKernel.Domain.Specifications;
using SharedKernel.Persistence.EfCore.Repositories;
using SharedKernel.Persistence.EfCore.Specifications;
using SharedKernel.Persistence.EfCore.Tests.TestFixtures;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Persistence.EfCore.Tests.Extensions;

/// <summary>
/// WO-053/P-338 (C-141/C-142/C-143): <see cref="IReadReplicaContextAccessor{TContext}"/>,
/// <see cref="EfCorePersistenceBuilder{TContext}.WithReadReplica"/>, and
/// <see cref="EfReadRepository{TAggregate,TId}"/>'s optional replica-routing constructor parameter.
/// </summary>
public sealed class ReadReplicaRoutingTests
{
    // A hand-rolled fake accessor for unit-testing EfReadRepository's own EffectiveContext routing
    // logic in isolation, without going through the full DI/ActivatorUtilities-based builder wiring.
    private sealed class FakeReadReplicaContextAccessor(SharedKernelDbContext replicaContext)
        : IReadReplicaContextAccessor<SharedKernelDbContext>
    {
        public SharedKernelDbContext GetEffectiveContext(SharedKernelDbContext primaryContext) =>
            primaryContext.Database.CurrentTransaction is not null ? primaryContext : replicaContext;
    }

    private sealed class ReplicaRoutedReadRepository(
        TestDbContext ctx,
        IReadReplicaContextAccessor<SharedKernelDbContext>? accessor)
        : EfReadRepository<TestAggregate, TestId>(ctx, new SpecificationEvaluator<TestAggregate>(), accessor);

    [Fact]
    public async Task GetEffectiveContext_NoActiveTransaction_RoutesReadToReplica()
    {
        // Arrange — primary and replica are two INDEPENDENT SQLite databases with distinguishable data.
        using var primary = TestDbContextFactory.CreateTestDbContext();
        primary.TestAggregates.Add(new TestAggregate(TestId.New(), "PrimaryOnly", new SystemClock()));
        await primary.SaveChangesAsync();

        using var replica = TestDbContextFactory.CreateTestDbContext();
        replica.TestAggregates.Add(new TestAggregate(TestId.New(), "ReplicaOnly", new SystemClock()));
        await replica.SaveChangesAsync();

        var accessor = new FakeReadReplicaContextAccessor(replica);
        var repo = new ReplicaRoutedReadRepository(primary, accessor);

        // Act
        var results = await repo.ListAsync(new AllSpecification<TestAggregate>());

        // Assert — the read observed the REPLICA's data, not the primary's.
        results.Should().ContainSingle(a => a.Name == "ReplicaOnly");
        results.Should().NotContain(a => a.Name == "PrimaryOnly");
    }

    [Fact]
    public async Task GetEffectiveContext_ActiveTransactionOnPrimary_NeverRoutesToReplica()
    {
        // Arrange
        using var primary = TestDbContextFactory.CreateTestDbContext();
        primary.TestAggregates.Add(new TestAggregate(TestId.New(), "PrimaryOnly", new SystemClock()));
        await primary.SaveChangesAsync();

        using var replica = TestDbContextFactory.CreateTestDbContext();
        replica.TestAggregates.Add(new TestAggregate(TestId.New(), "ReplicaOnly", new SystemClock()));
        await replica.SaveChangesAsync();

        var accessor = new FakeReadReplicaContextAccessor(replica);
        var repo = new ReplicaRoutedReadRepository(primary, accessor);

        // Act — an explicit transaction is active on the primary.
        await using var transaction = await primary.Database.BeginTransactionAsync();
        var results = await repo.ListAsync(new AllSpecification<TestAggregate>());
        await transaction.RollbackAsync();

        // Assert — routing must NEVER divert to the replica while a transaction is open.
        results.Should().ContainSingle(a => a.Name == "PrimaryOnly");
        results.Should().NotContain(a => a.Name == "ReplicaOnly");
    }

    [Fact]
    public async Task NoAccessorSupplied_ReadsTargetPrimaryDirectly_UnchangedBehavior()
    {
        // Arrange — every existing EfReadRepository subclass omitting the accessor parameter must
        // continue to behave identically (purely additive constructor parameter).
        using var primary = TestDbContextFactory.CreateTestDbContext();
        primary.TestAggregates.Add(new TestAggregate(TestId.New(), "PrimaryOnly", new SystemClock()));
        await primary.SaveChangesAsync();

        var repo = new ReplicaRoutedReadRepository(primary, accessor: null);

        // Act
        var results = await repo.ListAsync(new AllSpecification<TestAggregate>());

        // Assert
        results.Should().ContainSingle(a => a.Name == "PrimaryOnly");
    }

    [Fact]
    public void WithReadReplica_RegistersKeyedReplicaOptions_AndScopedAccessor()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services
            .AddSharedKernelEfCore<TestDbContext>(opts =>
                opts.UseSqlite("DataSource=:memory:")
                    .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
            .WithReadReplica(opts =>
                opts.UseSqlite("DataSource=:memory:")
                    .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
            .Build();

        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        // Assert
        var accessor = scope.ServiceProvider.GetService<IReadReplicaContextAccessor<SharedKernelDbContext>>();
        accessor.Should().NotBeNull();

        var replicaOptions = scope.ServiceProvider
            .GetKeyedService<DbContextOptions<TestDbContext>>(ReadReplicaKeys.ReplicaOptions);
        replicaOptions.Should().NotBeNull();
    }

    [Fact]
    public void WithReadReplica_SameScope_ReplicaContextInstanceIsCachedOncePerScope_NeverReconstructed()
    {
        // Arrange
        var services = new ServiceCollection();
        services
            .AddSharedKernelEfCore<TestDbContext>(opts =>
                opts.UseSqlite("DataSource=:memory:")
                    .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
            .WithReadReplica(opts =>
                opts.UseSqlite("DataSource=:memory:")
                    .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
            .Build();

        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var accessor = scope.ServiceProvider.GetRequiredService<IReadReplicaContextAccessor<SharedKernelDbContext>>();
        var primaryContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();

        // Act — call GetEffectiveContext twice within the SAME DI scope, against the SAME accessor
        // instance (accessor is itself scoped, so a second GetRequiredService call within this
        // scope resolves the identical accessor).
        var replicaFirstCall = accessor.GetEffectiveContext(primaryContext);
        var replicaSecondCall = accessor.GetEffectiveContext(primaryContext);

        // Assert — the SAME replica TContext instance is returned both times: constructed lazily
        // on first access, then cached for the remainder of the scope, never reconstructed per call.
        ReferenceEquals(replicaFirstCall, replicaSecondCall).Should().BeTrue();
    }

    [Fact]
    public void WithoutWithReadReplica_AccessorIsUnregistered()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act — no .WithReadReplica(...) call.
        services
            .AddSharedKernelEfCore<TestDbContext>(opts =>
                opts.UseSqlite("DataSource=:memory:")
                    .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
            .Build();

        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        // Assert
        var accessor = scope.ServiceProvider.GetService<IReadReplicaContextAccessor<SharedKernelDbContext>>();
        accessor.Should().BeNull();
    }
}
