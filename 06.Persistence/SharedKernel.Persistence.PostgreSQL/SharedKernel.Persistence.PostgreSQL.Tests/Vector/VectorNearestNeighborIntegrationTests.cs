using System.Linq.Expressions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SharedKernel.Domain.Specifications;
using SharedKernel.Persistence.EfCore.Specifications;
using SharedKernel.Persistence.PostgreSQL.Extensions;
using SharedKernel.Persistence.PostgreSQL.Vector;
using Testcontainers.PostgreSql;
using PgVector = Pgvector.Vector;

namespace SharedKernel.Persistence.PostgreSQL.Tests.Vector;

// ---------------------------------------------------------------------------
// Minimal EF Core entity + DbContext for the nearest-neighbor correctness proofs (T-118/T-119/
// T-120, WO-053/P-339). No SharedKernelDbContext/interceptors are needed — these tests exercise
// only SpecificationEvaluator<T>.GetQuery + VectorOrderingExpressions.ByDistance server-side.
// ---------------------------------------------------------------------------

public sealed class VectorTestProduct
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public PgVector Embedding { get; set; } = null!;
}

public sealed class VectorNearestNeighborTestDbContext : DbContext
{
    public DbSet<VectorTestProduct> Products => Set<VectorTestProduct>();

    public VectorNearestNeighborTestDbContext(DbContextOptions<VectorNearestNeighborTestDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<VectorTestProduct>(b =>
        {
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).ValueGeneratedOnAdd();
            b.HasVectorColumn(e => e.Embedding, dimensions: 3);
        });
    }
}

/// <summary>
/// A specification composing <see cref="VectorOrderingExpressions.ByDistance{TAggregate}"/>
/// alongside an optional <see cref="Specification{T}.Criteria"/> filter and top-K paging — proving
/// zero <see cref="SpecificationEvaluator{T}"/> special-casing is needed for vector ordering to
/// compose with the rest of the specification pipeline (T-120).
/// </summary>
internal sealed class NearestByDistanceSpec : Specification<VectorTestProduct>
{
    public NearestByDistanceSpec(
        PgVector queryVector,
        VectorDistanceMetric metric,
        Expression<Func<VectorTestProduct, bool>>? criteria = null,
        int? take = null)
    {
        if (criteria is not null)
            AddCriteria(criteria);

        ApplyOrderBy(VectorOrderingExpressions.ByDistance<VectorTestProduct>(p => p.Embedding, queryVector, metric));

        if (take is not null)
            ApplyPaging(skip: 0, take: take.Value);
    }
}

/// <summary>
/// T-118/T-119/T-120 (WO-053/P-339, C-144/C-145): real PostgreSQL Testcontainer proofs that
/// <see cref="VectorOrderingExpressions.ByDistance{TAggregate}"/> produces genuinely correct,
/// server-side nearest-neighbor ordering — <see cref="VectorOrderingExpressionsTests"/>
/// (same test project, no container) only proves the built <see cref="Expression"/> tree's SHAPE;
/// this class proves the resulting QUERY BEHAVIOR against a real <c>pgvector</c>-enabled database.
/// </summary>
/// <remarks>
/// Uses its own dedicated <c>pgvector/pgvector:pg16</c> container rather than the shared
/// <see cref="SharedKernel.Testing.Containers.PostgreSqlContainerFixture"/> — the pgvector
/// extension binary is absent from that fixture's plain <c>postgres:16.4</c> image, mirroring
/// <see cref="SharedKernel.Persistence.PostgreSQL.Tests.Integration.PostgreSQLIntegrationTests"/>'s
/// identical, already-established constraint.
/// </remarks>
public sealed class VectorNearestNeighborIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("pgvector/pgvector:pg16").Build();

    private string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    private async Task<VectorNearestNeighborTestDbContext> CreateContextAsync()
    {
        // Ensure the pgvector extension exists BEFORE the EF Core-managed Npgsql connection pool
        // opens its first connection — Npgsql resolves the "vector" type OID only once per
        // data-source's lifetime (at first connection open), so the extension must already exist
        // in the database at that point or every subsequent Pgvector.Vector-typed parameter write
        // fails with "Cannot resolve 'vector' to a fully qualified datatype name" for the rest of
        // that pool's life. Mirrors PostgreSQLIntegrationTests's own raw-ADO.NET precedent, run
        // against a throwaway, separate connection/data source used ONLY for this DDL statement.
        await using (var dataSource = new NpgsqlDataSourceBuilder(ConnectionString).Build())
        await using (var conn = await dataSource.OpenConnectionAsync())
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "CREATE EXTENSION IF NOT EXISTS vector;";
            await cmd.ExecuteNonQueryAsync();
        }

        var builder = new DbContextOptionsBuilder<VectorNearestNeighborTestDbContext>();
        builder.UsePostgreSQL(ConnectionString);
        var options = builder.Options;

        var ctx = new VectorNearestNeighborTestDbContext(options);
        await ctx.Database.EnsureCreatedAsync();
        return ctx;
    }

    [Fact]
    public async Task ByDistance_Cosine_ReturnsRowsInGenuineAscendingCosineDistanceOrder_ServerSideOrderBy()
    {
        // Arrange — manually-computed cosine distances from queryVector = [1,0,0]:
        //   A = [1,0,0] -> cosine similarity 1      -> distance 0
        //   B = [1,1,0] -> cosine similarity 1/sqrt2 -> distance ~0.2929
        //   C = [0,1,0] -> cosine similarity 0      -> distance 1
        // Seeded out of ascending-distance order (C, A, B) to prove the ORDER BY genuinely
        // reorders the result set rather than coincidentally matching insertion order.
        await using var ctx = await CreateContextAsync();
        var queryVector = new PgVector(new float[] { 1f, 0f, 0f });

        ctx.Products.AddRange(
            new VectorTestProduct { Name = "C", Category = "X", Embedding = new PgVector(new float[] { 0f, 1f, 0f }) },
            new VectorTestProduct { Name = "A", Category = "X", Embedding = new PgVector(new float[] { 1f, 0f, 0f }) },
            new VectorTestProduct { Name = "B", Category = "X", Embedding = new PgVector(new float[] { 1f, 1f, 0f }) });
        await ctx.SaveChangesAsync();

        var evaluator = new SpecificationEvaluator<VectorTestProduct>();
        var spec = new NearestByDistanceSpec(queryVector, VectorDistanceMetric.Cosine);
        var query = evaluator.GetQuery(ctx.Products, spec);

        // Assert — a server-side ORDER BY ... <=> ... clause, never client-side evaluation.
        query.ToQueryString().Should().Contain("<=>");

        var results = await query.ToListAsync();
        results.Select(p => p.Name).Should().Equal("A", "B", "C");
    }

    [Fact]
    public async Task ByDistance_L2_ReturnsRowsInGenuineAscendingL2DistanceOrder_ServerSideOrderBy()
    {
        // Arrange — manually-computed L2 (Euclidean) distances from the origin queryVector = [0,0,0]:
        //   Near = [1,0,0] -> distance 1
        //   Mid  = [2,0,0] -> distance 2
        //   Far  = [3,0,0] -> distance 3
        // Seeded in REVERSE distance order to prove the ORDER BY genuinely reorders.
        await using var ctx = await CreateContextAsync();
        var queryVector = new PgVector(new float[] { 0f, 0f, 0f });

        ctx.Products.AddRange(
            new VectorTestProduct { Name = "Far", Category = "X", Embedding = new PgVector(new float[] { 3f, 0f, 0f }) },
            new VectorTestProduct { Name = "Mid", Category = "X", Embedding = new PgVector(new float[] { 2f, 0f, 0f }) },
            new VectorTestProduct { Name = "Near", Category = "X", Embedding = new PgVector(new float[] { 1f, 0f, 0f }) });
        await ctx.SaveChangesAsync();

        var evaluator = new SpecificationEvaluator<VectorTestProduct>();
        var spec = new NearestByDistanceSpec(queryVector, VectorDistanceMetric.L2);
        var query = evaluator.GetQuery(ctx.Products, spec);

        // Assert — a server-side ORDER BY ... <-> ... clause.
        query.ToQueryString().Should().Contain("<->");

        var results = await query.ToListAsync();
        results.Select(p => p.Name).Should().Equal("Near", "Mid", "Far");
    }

    [Fact]
    public async Task ByDistance_ComposedWithCriteriaAndTake_FiltersAndCapsWhileOrderingRemainsCorrect()
    {
        // Arrange — "C" (Category "Y") has the smallest RAW distance (0.5) but must be excluded by
        // the Category == "X" filter; among Category "X" rows, only the nearest 2 (top-K via Take)
        // must be returned, in correct distance order.
        await using var ctx = await CreateContextAsync();
        var queryVector = new PgVector(new float[] { 0f, 0f, 0f });

        ctx.Products.AddRange(
            new VectorTestProduct { Name = "A", Category = "X", Embedding = new PgVector(new float[] { 1f, 0f, 0f }) }, // dist 1
            new VectorTestProduct { Name = "B", Category = "X", Embedding = new PgVector(new float[] { 2f, 0f, 0f }) }, // dist 2
            new VectorTestProduct { Name = "C", Category = "Y", Embedding = new PgVector(new float[] { 0.5f, 0f, 0f }) }, // dist 0.5, excluded by filter
            new VectorTestProduct { Name = "D", Category = "X", Embedding = new PgVector(new float[] { 3f, 0f, 0f }) }, // dist 3, beyond top-2
            new VectorTestProduct { Name = "E", Category = "X", Embedding = new PgVector(new float[] { 4f, 0f, 0f }) }); // dist 4, beyond top-2
        await ctx.SaveChangesAsync();

        var evaluator = new SpecificationEvaluator<VectorTestProduct>();
        var spec = new NearestByDistanceSpec(
            queryVector,
            VectorDistanceMetric.L2,
            criteria: p => p.Category == "X",
            take: 2);
        var query = evaluator.GetQuery(ctx.Products, spec);

        // Act
        var results = await query.ToListAsync();

        // Assert — filter honored (never "C"), result capped at K == 2, distance ordering correct.
        results.Should().HaveCount(2);
        results.Select(p => p.Name).Should().Equal("A", "B");
    }
}
