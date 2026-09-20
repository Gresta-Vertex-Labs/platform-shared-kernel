using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Npgsql;
using SharedKernel.Contracts.Pagination;
using SharedKernel.Domain.Aggregates;
using SharedKernel.Domain.Specifications;
using SharedKernel.Domain.StronglyTypedIds;
using SharedKernel.Persistence.EfCore.Configurations;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Conversions;
using SharedKernel.Persistence.EfCore.Interceptors;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Persistence;
using SharedKernel.Persistence.EfCore.Repositories;
using SharedKernel.Persistence.EfCore.Specifications;
using SharedKernel.Persistence.PostgreSQL.Extensions;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Testing.Containers;

namespace SharedKernel.Persistence.PostgreSQL.Tests.Integration;

// ---------------------------------------------------------------------------
// Proves keyset (cursor/seek) pagination with a DateTimeOffset sort key — the
// documented, canonical KeysetSpecification<T,TKey> shape (see its own <example>) — genuinely
// works against real PostgreSQL. SQLite's EF Core provider cannot ORDER BY a DateTimeOffset column
// at all (a provider limitation, proven separately in SharedKernel.Persistence.EfCore.Tests using a
// `long` sort key instead), so this is the only place the platform's actual intended usage shape is
// exercised end to end.
// ---------------------------------------------------------------------------

public sealed record KeysetPgId(Guid Value) : StronglyTypedId<Guid>(Value)
{
    public static KeysetPgId New() => new(Guid.NewGuid());
}

public sealed class KeysetPgAggregate : AggregateRoot<KeysetPgId>
{
    public string Name { get; private set; } = string.Empty;
    public DateTimeOffset CreatedOn { get; private set; }

    public KeysetPgAggregate(KeysetPgId id, string name, DateTimeOffset createdOn, IClock clock)
        : base(id, clock)
    {
        Name = name;
        CreatedOn = createdOn;
    }

    protected KeysetPgAggregate() { } // ORM path
}

public sealed class KeysetPgAggregateConfig : EntityTypeConfigurationBase<KeysetPgAggregate, KeysetPgId>
{
    public override void Configure(EntityTypeBuilder<KeysetPgAggregate> builder)
    {
        base.Configure(builder);
        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
        builder.Property(e => e.CreatedOn).IsRequired();
    }
}

public sealed class KeysetPgDbContext : SharedKernelDbContext
{
    public DbSet<KeysetPgAggregate> Aggregates => Set<KeysetPgAggregate>();

    public KeysetPgDbContext(
        DbContextOptions<KeysetPgDbContext> options,
        PersistenceContextDependencies dependencies)
            : base(options, dependencies)
    {
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.ConfigureStronglyTypedId<KeysetPgId, Guid>();
        base.ConfigureConventions(configurationBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new KeysetPgAggregateConfig());
    }
}

internal sealed class KeysetPgReadRepository(KeysetPgDbContext ctx)
    : EfReadRepository<KeysetPgAggregate, KeysetPgId>(ctx, new SpecificationEvaluator<KeysetPgAggregate>())
{
}

internal sealed class KeysetPgByCreatedOnSpec : KeysetSpecification<KeysetPgAggregate, DateTimeOffset>
{
    public KeysetPgByCreatedOnSpec(DateTimeOffset? afterKey, object? afterId, int take)
        : base(a => a.CreatedOn, a => a.Id, afterKey, afterId, descending: false, take)
    {
    }
}

/// <remarks>
/// Shares the <see cref="PostgreSqlContainerFixture"/> registered by
/// <see cref="PostgreSqlTestCollection"/> instead of starting its own dedicated container — see
/// <see cref="ConcurrencyIntegrationTests"/>'s identical remark for why a uniquely-named database is
/// targeted rather than the fixture's shared default database.
/// </remarks>
[Collection("PostgreSQL")]
public sealed class KeysetPaginationIntegrationTests
{
    private const string DatabaseName = "sk_persistence_keyset";

    private readonly PostgreSqlContainerFixture _fixture;

    public KeysetPaginationIntegrationTests(PostgreSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    private string ConnectionString =>
        new NpgsqlConnectionStringBuilder(_fixture.ConnectionString) { Database = DatabaseName }.ConnectionString;

    private static KeysetPgDbContext CreateContext(string connectionString)
    {
        var builder = new DbContextOptionsBuilder<KeysetPgDbContext>();
        builder.UsePostgreSQL(connectionString);
        var options = builder.Options;

        var actorContext = new FakeAuditActorContext();
        var clock = new FakeClock();

        var audit = new AuditInterceptor(actorContext, clock);
        var softDelete = new SoftDeleteInterceptor(actorContext, clock);
        var concurrency = new ConcurrencyInterceptor();

        return new KeysetPgDbContext(options, new PersistenceContextDependencies(audit, softDelete, concurrency));
    }

    [Fact]
    public async Task ListKeysetAsync_WithDateTimeOffsetSortKey_WalksAllPagesInOrder_AgainstRealPostgres()
    {
        // Arrange
        var baseTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        await using (var setupCtx = CreateContext(ConnectionString))
        {
            await setupCtx.Database.EnsureCreatedAsync();

            for (var i = 0; i < 5; i++)
            {
                setupCtx.Aggregates.Add(new KeysetPgAggregate(
                    KeysetPgId.New(), $"Item{i}", baseTime.AddMinutes(i), new SystemClock()));
                await setupCtx.SaveChangesAsync();
            }
        }

        await using var ctx = CreateContext(ConnectionString);
        var repo = new KeysetPgReadRepository(ctx);

        var allItems = new List<string>();
        DateTimeOffset? afterKey = null;
        object? afterId = null;
        bool hasMore;
        var pageCount = 0;

        // Act — walk every page via the returned cursor, using a genuine DateTimeOffset sort key
        // against real PostgreSQL (not SQLite, which cannot ORDER BY DateTimeOffset at all).
        do
        {
            var spec = new KeysetPgByCreatedOnSpec(afterKey, afterId, take: 2);
            var page = await repo.ListKeysetAsync(spec);

            allItems.AddRange(page.Items.Select(i => i.Name));
            if (page.NextCursor is null)
            {
                afterKey = null;
                afterId = null;
            }
            else
            {
                var decoded = PageCursor.Decode<DateTimeOffset, KeysetPgId>(page.NextCursor);
                decoded.IsSuccess.Should().BeTrue();
                afterKey = decoded.Value.Key;
                afterId = decoded.Value.Id;
            }

            hasMore = page.HasMore;
            pageCount++;
        } while (hasMore && pageCount < 10);

        // Assert
        allItems.Should().ContainInOrder("Item0", "Item1", "Item2", "Item3", "Item4");
        pageCount.Should().Be(3);
    }
}
