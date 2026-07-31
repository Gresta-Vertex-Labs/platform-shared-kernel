using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Options;
using NSubstitute;
using SharedKernel.Core.Exceptions;
using SharedKernel.Domain.Aggregates;
using SharedKernel.Domain.StronglyTypedIds;
using SharedKernel.Persistence.EfCore.Configurations;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Conversions;
using SharedKernel.Persistence.EfCore.Interceptors;
using SharedKernel.Persistence.EfCore.Options;
using SharedKernel.Persistence.PostgreSQL.Extensions;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Errors;
using SharedKernel.Security.Abstractions.Abstractions;
using Testcontainers.PostgreSql;

namespace SharedKernel.Persistence.PostgreSQL.Tests.Integration;

// ---------------------------------------------------------------------------
// WO-051/P-315 (D-66) — the real PostgreSQL xmin concurrency-conflict proof.
// ---------------------------------------------------------------------------

public sealed record ConcurrentPgId(Guid Value) : StronglyTypedId<Guid>(Value)
{
    public static ConcurrentPgId New() => new(Guid.NewGuid());
}

public sealed class ConcurrentPgAggregate : FullAuditableAggregateRoot<ConcurrentPgId>
{
    public string Name { get; private set; } = string.Empty;

    public ConcurrentPgAggregate(ConcurrentPgId id, string name, IClock clock) : base(id, clock)
    {
        Name = name;
    }

    protected ConcurrentPgAggregate() { } // ORM path

    protected override void OnDelete() { }

    public void Rename(string name) => Name = name;
}

public sealed class ConcurrentPgAggregateConfig : EntityTypeConfigurationBase<ConcurrentPgAggregate, ConcurrentPgId>
{
    public override void Configure(EntityTypeBuilder<ConcurrentPgAggregate> builder)
    {
        base.Configure(builder);
        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
    }
}

public sealed class ConcurrencyTestDbContext : SharedKernelDbContext
{
    public DbSet<ConcurrentPgAggregate> Aggregates => Set<ConcurrentPgAggregate>();

    public ConcurrencyTestDbContext(
        DbContextOptions<ConcurrencyTestDbContext> options,
        AuditInterceptor auditInterceptor,
        SoftDeleteInterceptor softDeleteInterceptor,
        ConcurrencyInterceptor concurrencyInterceptor)
        : base(options, auditInterceptor, softDeleteInterceptor, concurrencyInterceptor)
    {
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.ConfigureStronglyTypedId<ConcurrentPgId, Guid>();
        base.ConfigureConventions(configurationBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Deliberately does not call base.OnModelCreating's assembly scan — applies only this
        // context's own entity configuration, mirroring SharedKernel.Persistence.EfCore.Tests'
        // TestDbContext precedent.
        modelBuilder.ApplyConfiguration(new ConcurrentPgAggregateConfig());
    }
}

/// <summary>
/// T-38 successor (WO-051/P-315, D-66): proves the genuine, working PostgreSQL optimistic
/// concurrency mechanism — <c>XminConcurrencyTokenConvention</c> binding
/// <see cref="SharedKernel.Domain.Abstractions.IHasConcurrency.RowVersion"/> to the real
/// <c>xmin</c> system column, auto-wired by <c>UsePostgreSQL()</c> — end to end against a real
/// PostgreSQL Testcontainer.
/// </summary>
[Collection("PostgreSQL")]
public sealed class ConcurrencyIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    private static ConcurrencyTestDbContext CreateContext(string connectionString)
    {
        // UsePostgreSQL() returns the non-generic DbContextOptionsBuilder (it operates on the
        // shared base type so it composes with both generic and non-generic builders) — call it
        // as a statement against the generic builder instance, then read .Options off that same
        // generic instance to get a properly-typed DbContextOptions<ConcurrencyTestDbContext>.
        var builder = new DbContextOptionsBuilder<ConcurrencyTestDbContext>();
        builder.UsePostgreSQL(connectionString);
        var options = builder.Options;

        var userContext = Substitute.For<IUserContext>();
        userContext.IsAuthenticated.Returns(true);
        userContext.UserId.Returns(Guid.NewGuid());

        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);

        var serviceOptions = Options.Create(new PersistenceServiceOptions());

        var audit = new AuditInterceptor(userContext, clock, serviceOptions);
        var softDelete = new SoftDeleteInterceptor(userContext, clock, serviceOptions);
        var concurrency = new ConcurrencyInterceptor();

        return new ConcurrencyTestDbContext(options, audit, softDelete, concurrency);
    }

    [Fact]
    public async Task SaveChanges_ConcurrentUpdates_FirstSucceedsAndXminGenuinelyChanges_SecondThrowsConflictException()
    {
        // Arrange — seed one row.
        var id = ConcurrentPgId.New();

        await using (var setupCtx = CreateContext(ConnectionString))
        {
            await setupCtx.Database.EnsureCreatedAsync();
            setupCtx.Aggregates.Add(new ConcurrentPgAggregate(id, "Original", new SystemClock()));
            await setupCtx.SaveChangesAsync();
        }

        // Two independent DbContext instances load the SAME row.
        await using var ctx1 = CreateContext(ConnectionString);
        await using var ctx2 = CreateContext(ConnectionString);

        var entity1 = await ctx1.Aggregates.FirstAsync(e => e.Id == id);
        var entity2 = await ctx2.Aggregates.FirstAsync(e => e.Id == id);

        var originalRowVersion = entity1.RowVersion;

        // Both mutate different properties.
        entity1.Rename("Modified by ctx1");
        entity2.Rename("Modified by ctx2");

        // Act / Assert — the first SaveChangesAsync succeeds, and the row's xmin genuinely changes.
        await ctx1.SaveChangesAsync();
        entity1.RowVersion.Should().NotBeEquivalentTo(originalRowVersion,
            "PostgreSQL assigns a fresh xmin to the row on every successful UPDATE");

        // Re-query independently (a third, unrelated context) to prove the change is real and
        // server-side, not merely an artifact of ctx1's own tracked in-memory state.
        await using var verifyCtx = CreateContext(ConnectionString);
        var verifyEntity = await verifyCtx.Aggregates.AsNoTracking().FirstAsync(e => e.Id == id);
        verifyEntity.Name.Should().Be("Modified by ctx1");
        verifyEntity.RowVersion.Should().BeEquivalentTo(entity1.RowVersion);
        verifyEntity.RowVersion.Should().NotBeEquivalentTo(originalRowVersion);

        // The second SaveChangesAsync — ctx2 is still holding the ORIGINAL, now-stale xmin as its
        // tracked original value — must fail with a genuine PostgreSQL concurrency conflict,
        // caught and rethrown by SharedKernelDbContext (via ConcurrencyInterceptor.TryTranslate)
        // as ConflictException.
        Func<Task> act = () => ctx2.SaveChangesAsync();
        var exception = await act.Should().ThrowAsync<ConflictException>();
        exception.Which.Error.Type.Should().Be(ErrorType.Conflict);
        exception.Which.InnerException.Should().BeOfType<DbUpdateConcurrencyException>();
    }
}
