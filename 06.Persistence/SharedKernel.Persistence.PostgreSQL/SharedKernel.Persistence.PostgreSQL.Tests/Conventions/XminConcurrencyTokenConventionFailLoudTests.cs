using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Interceptors;
using SharedKernel.Persistence.PostgreSQL.Extensions;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Persistence;

namespace SharedKernel.Persistence.PostgreSQL.Tests.Conventions;

// A deliberately misconfigured entity: implements IHasConcurrency (declaring intent to participate
// in optimistic concurrency) but its configuration never calls.IsConcurrencyToken() — bypasses
// EntityTypeConfigurationBase entirely, the exact shape XminConcurrencyTokenConvention's fail-loud
// guard exists to catch.
public sealed class UnconfiguredConcurrencyEntity : SharedKernel.Domain.Entities.Entity<Guid>, IHasConcurrency
{
    public byte[] RowVersion { get; private set; } = [];

    public UnconfiguredConcurrencyEntity(Guid id)
        : base(id)
    {
    }

    private UnconfiguredConcurrencyEntity()
    {
    }
}

public sealed class UnconfiguredConcurrencyEntityConfig : IEntityTypeConfiguration<UnconfiguredConcurrencyEntity>
{
    public void Configure(EntityTypeBuilder<UnconfiguredConcurrencyEntity> builder)
    {
        builder.HasKey(e => e.Id);
        // Deliberately NOT calling builder.Property(e => e.RowVersion).IsConcurrencyToken().
    }
}

public sealed class UnconfiguredConcurrencyTestDbContext : SharedKernelDbContext
{
    public DbSet<UnconfiguredConcurrencyEntity> Entities => Set<UnconfiguredConcurrencyEntity>();

    public UnconfiguredConcurrencyTestDbContext(
        DbContextOptions<UnconfiguredConcurrencyTestDbContext> options,
        PersistenceContextDependencies dependencies)
            : base(options, dependencies)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new UnconfiguredConcurrencyEntityConfig());
    }
}

/// <summary>
/// <see cref="SharedKernel.Persistence.PostgreSQL.Conventions.XminConcurrencyTokenConvention"/>
/// fails loudly, at model-building time, for an <see cref="IHasConcurrency"/> entity whose
/// <c>RowVersion</c> property was never marked <c>.IsConcurrencyToken()</c> — never silently skipped.
/// </summary>
public sealed class XminConcurrencyTokenConventionFailLoudTests
{
    [Fact]
    public void Model_UnconfiguredConcurrencyEntity_ThrowsAtModelBuildingTime()
    {
        var builder = new DbContextOptionsBuilder<UnconfiguredConcurrencyTestDbContext>();
        builder.UsePostgreSQL("Host=localhost;Database=xmin_failloud_test;Username=test;Password=test");
        var options = builder.Options;

        var actorContext = new FakeAuditActorContext();
        var clock = new FakeClock();
        var audit = new AuditInterceptor(actorContext, clock);
        var softDelete = new SoftDeleteInterceptor(actorContext, clock);
        var concurrency = new ConcurrencyInterceptor();

        using var ctx = new UnconfiguredConcurrencyTestDbContext(options, new PersistenceContextDependencies(audit, softDelete, concurrency));

        var act = () => ctx.Model.GetEntityTypes().ToList();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*UnconfiguredConcurrencyEntity*")
                .WithMessage("*IsConcurrencyToken*");
    }
}
