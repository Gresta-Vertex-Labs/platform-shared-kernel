using System.Data.Common;
using System.Linq.Expressions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SharedKernel.Domain.Aggregates;
using SharedKernel.Domain.Specifications;
using SharedKernel.Domain.StronglyTypedIds;
using SharedKernel.Persistence.Abstractions.Specifications;
using SharedKernel.Persistence.EfCore.Configurations;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Conversions;
using SharedKernel.Persistence.EfCore.Options;
using SharedKernel.Persistence.EfCore.Specifications;
using SharedKernel.Persistence.EfCore.Tests.TestFixtures;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Persistence.EfCore.Tests.Specifications;

// ---------------------------------------------------------------------------
// AsSplitQuery genuinely issuing multiple SQL statements for a
// specification with TWO SIBLING collection Includes, for both GetQuery (entity materialization)
// and GetProjectedQuery (DTO projection), with correct duplicate-free materialized results.
// ---------------------------------------------------------------------------

public sealed record SplitAggId(Guid Value) : StronglyTypedId<Guid>(Value)
{
    public static SplitAggId New() => new(Guid.NewGuid());
}

public sealed class SplitTagChild
{
    public Guid Id { get; private set; }
    public string Text { get; private set; } = string.Empty;
    public SplitAggId ParentId { get; private set; } = null!;

    public SplitTagChild(Guid id, string text, SplitAggId parentId)
    {
        Id = id;
        Text = text;
        ParentId = parentId;
    }

    protected SplitTagChild() { } // ORM path
}

public sealed class SplitNoteChild
{
    public Guid Id { get; private set; }
    public string Text { get; private set; } = string.Empty;
    public SplitAggId ParentId { get; private set; } = null!;

    public SplitNoteChild(Guid id, string text, SplitAggId parentId)
    {
        Id = id;
        Text = text;
        ParentId = parentId;
    }

    protected SplitNoteChild() { } // ORM path
}

public sealed class SplitAggregate : AggregateRoot<SplitAggId>
{
    public string Name { get; private set; } = string.Empty;
    public ICollection<SplitTagChild> Tags { get; private set; } = new List<SplitTagChild>();
    public ICollection<SplitNoteChild> Notes { get; private set; } = new List<SplitNoteChild>();

    public SplitAggregate(SplitAggId id, string name, IClock clock) : base(id, clock)
    {
        Name = name;
    }

    protected SplitAggregate() { } // ORM path
}

public sealed class SplitAggregateConfig : EntityTypeConfigurationBase<SplitAggregate, SplitAggId>
{
    public override void Configure(EntityTypeBuilder<SplitAggregate> builder)
    {
        base.Configure(builder);
        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
        builder.HasMany(e => e.Tags).WithOne().HasForeignKey(t => t.ParentId).IsRequired();
        builder.HasMany(e => e.Notes).WithOne().HasForeignKey(n => n.ParentId).IsRequired();
    }
}

public sealed class SplitTagChildConfig : IEntityTypeConfiguration<SplitTagChild>
{
    public void Configure(EntityTypeBuilder<SplitTagChild> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Text).HasMaxLength(200).IsRequired();
        builder.Property(e => e.ParentId).HasConversion<Guid>(id => id.Value, v => new SplitAggId(v));
    }
}

public sealed class SplitNoteChildConfig : IEntityTypeConfiguration<SplitNoteChild>
{
    public void Configure(EntityTypeBuilder<SplitNoteChild> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Text).HasMaxLength(200).IsRequired();
        builder.Property(e => e.ParentId).HasConversion<Guid>(id => id.Value, v => new SplitAggId(v));
    }
}

public sealed class SplitQueryDbContext : SharedKernelDbContext
{
    public DbSet<SplitAggregate> SplitAggregates => Set<SplitAggregate>();
    public DbSet<SplitTagChild> SplitTags => Set<SplitTagChild>();
    public DbSet<SplitNoteChild> SplitNotes => Set<SplitNoteChild>();

    public SplitQueryDbContext(
        DbContextOptions<SplitQueryDbContext> options,
        PersistenceContextDependencies dependencies)
            : base(options, dependencies)
    {
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new SplitAggregateConfig());
        modelBuilder.ApplyConfiguration(new SplitTagChildConfig());
        modelBuilder.ApplyConfiguration(new SplitNoteChildConfig());
    }
}

internal sealed class SplitAggregateWithTwoIncludesSpec : Specification<SplitAggregate>
{
    public SplitAggregateWithTwoIncludesSpec(bool split)
    {
        AddInclude(e => e.Tags);
        AddInclude(e => e.Notes);
        if (split)
            ApplySplitQuery();
    }
}

public sealed record SplitProjectionDto(string Name, List<string> TagTexts, List<string> NoteTexts);

internal sealed class SplitAggregateProjectionSpec
    : Specification<SplitAggregate>, IProjectionSpecification<SplitAggregate, SplitProjectionDto>
{
    public Expression<Func<SplitAggregate, SplitProjectionDto>> Selector { get; } =
        e => new SplitProjectionDto(
            e.Name,
            e.Tags.Select(t => t.Text).ToList(),
            e.Notes.Select(n => n.Text).ToList());

    public SplitAggregateProjectionSpec(bool split)
    {
        AddInclude(e => e.Tags);
        AddInclude(e => e.Notes);
        if (split)
            ApplySplitQuery();
    }
}

/// <summary>Counts every SQL statement executed via <c>ExecuteReader(Async)</c> for one logical query.</summary>
internal sealed class CommandCountingInterceptor : DbCommandInterceptor
{
    public int ReaderCommandCount { get; private set; }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        ReaderCommandCount++;
        return base.ReaderExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        ReaderCommandCount++;
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }
}

public sealed class AsSplitQueryMultiStatementTests
{
    private static (SplitQueryDbContext Ctx, CommandCountingInterceptor Counter) CreateContext()
    {
        var counter = new CommandCountingInterceptor();

        var actorContext = new SharedKernel.Testing.Persistence.FakeAuditActorContext();
        var clock = new SystemClock();

        var options = new DbContextOptionsBuilder<SplitQueryDbContext>()
            .UseSqlite("DataSource=:memory:")
            .AddInterceptors(counter)
            .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;

        var audit = PersistenceContextDependencies.Create(actorContext, clock);

        var ctx = new SplitQueryDbContext(options, audit);
        ctx.Database.OpenConnection();
        ctx.Database.EnsureCreated();

        return (ctx, counter);
    }

    private static async Task SeedAsync(SplitQueryDbContext ctx)
    {
        var id = SplitAggId.New();
        var aggregate = new SplitAggregate(id, "Parent1", new SystemClock());
        ctx.SplitAggregates.Add(aggregate);
        ctx.SplitTags.AddRange(
            new SplitTagChild(Guid.NewGuid(), "TagA", id),
            new SplitTagChild(Guid.NewGuid(), "TagB", id));
        ctx.SplitNotes.AddRange(
            new SplitNoteChild(Guid.NewGuid(), "NoteA", id),
            new SplitNoteChild(Guid.NewGuid(), "NoteB", id),
            new SplitNoteChild(Guid.NewGuid(), "NoteC", id));
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
    }

    // -----------------------------------------------------------------------
    // T-72 — GetQuery (entity materialization)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetQuery_AsSplitQueryTrue_TwoCollectionIncludes_IssuesMultipleStatements_CorrectResult()
    {
        var (ctx, counter) = CreateContext();
        using var disposeCtx = ctx;
        await SeedAsync(ctx);
        // Baseline AFTER seeding — SQLite's provider executes INSERT statements via ExecuteReader
        // (RETURNING-based), so the seed inserts themselves inflate ReaderCommandCount; only the
        // DELTA caused by the query under test is meaningful.
        var baseline = counter.ReaderCommandCount;

        var evaluator = new SpecificationEvaluator<SplitAggregate>();
        var spec = new SplitAggregateWithTwoIncludesSpec(split: true);

        var query = evaluator.GetQuery(ctx.SplitAggregates, spec);
        var results = await query.ToListAsync();

        // Correct, duplicate-free materialization — no Cartesian-product row artifacts.
        results.Should().HaveCount(1);
        results[0].Tags.Should().HaveCount(2);
        results[0].Notes.Should().HaveCount(3);
        results[0].Tags.Select(t => t.Text).Should().BeEquivalentTo(["TagA", "TagB"]);
        results[0].Notes.Select(n => n.Text).Should().BeEquivalentTo(["NoteA", "NoteB", "NoteC"]);

        (counter.ReaderCommandCount - baseline).Should().BeGreaterThan(1,
            "AsSplitQuery=true with two sibling collection Includes must issue MORE THAN ONE SQL statement");
    }

    [Fact]
    public async Task GetQuery_AsSplitQueryFalse_TwoCollectionIncludes_IssuesSingleStatement()
    {
        var (ctx, counter) = CreateContext();
        using var disposeCtx = ctx;
        await SeedAsync(ctx);
        var baseline = counter.ReaderCommandCount;

        var evaluator = new SpecificationEvaluator<SplitAggregate>();
        var spec = new SplitAggregateWithTwoIncludesSpec(split: false);

        var query = evaluator.GetQuery(ctx.SplitAggregates, spec);
        var results = await query.ToListAsync();

        results.Should().HaveCount(1);
        results[0].Tags.Should().HaveCount(2);
        results[0].Notes.Should().HaveCount(3);

        (counter.ReaderCommandCount - baseline).Should().Be(1,
            "the default (AsSplitQuery=false) query plan issues exactly one joined SQL statement");
    }

    // -----------------------------------------------------------------------
    // T-74 — GetProjectedQuery (DTO projection)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetProjectedQuery_AsSplitQueryTrue_TwoCollectionIncludes_IssuesMultipleStatements_CorrectDtoShape()
    {
        var (ctx, counter) = CreateContext();
        using var disposeCtx = ctx;
        await SeedAsync(ctx);
        var baseline = counter.ReaderCommandCount;

        var evaluator = new SpecificationEvaluator<SplitAggregate>();
        var spec = new SplitAggregateProjectionSpec(split: true);

        var query = evaluator.GetProjectedQuery(ctx.SplitAggregates, spec);
        var results = await query.ToListAsync();

        results.Should().HaveCount(1);
        results[0].Name.Should().Be("Parent1");
        results[0].TagTexts.Should().BeEquivalentTo(["TagA", "TagB"]);
        results[0].NoteTexts.Should().BeEquivalentTo(["NoteA", "NoteB", "NoteC"]);

        (counter.ReaderCommandCount - baseline).Should().BeGreaterThan(1,
            "GetProjectedQuery must honor AsSplitQuery for a projection spec with two collection Includes");
    }

    [Fact]
    public async Task GetProjectedQuery_AsSplitQueryFalse_TwoCollectionIncludes_CorrectDtoShape()
    {
        var (ctx, _) = CreateContext();
        using var disposeCtx = ctx;
        await SeedAsync(ctx);

        var evaluator = new SpecificationEvaluator<SplitAggregate>();
        var spec = new SplitAggregateProjectionSpec(split: false);

        var query = evaluator.GetProjectedQuery(ctx.SplitAggregates, spec);
        var results = await query.ToListAsync();

        results.Should().HaveCount(1);
        results[0].Name.Should().Be("Parent1");
        results[0].TagTexts.Should().BeEquivalentTo(["TagA", "TagB"]);
        results[0].NoteTexts.Should().BeEquivalentTo(["NoteA", "NoteB", "NoteC"]);
    }
}
