using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.Aggregates;
using SharedKernel.Domain.Specifications;
using SharedKernel.Domain.StronglyTypedIds;
using SharedKernel.Persistence.EfCore.Configurations;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Conversions;
using SharedKernel.Persistence.EfCore.Interceptors;
using SharedKernel.Persistence.EfCore.Specifications;
using SharedKernel.Persistence.EfCore.Tests.TestFixtures;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Persistence.EfCore.Tests.Specifications;

// ---------------------------------------------------------------------------
// T-37: SpecificationEvaluator<T> string-include tests
// ---------------------------------------------------------------------------

// Test entities with navigation property
public sealed record ParentId(Guid Value) : StronglyTypedId<Guid>(Value)
{
    public static ParentId New() => new(Guid.NewGuid());
}

public sealed record ChildId(Guid Value) : StronglyTypedId<Guid>(Value)
{
    public static ChildId New() => new(Guid.NewGuid());
}

public sealed class ChildEntity
{
    public ChildId Id { get; private set; } = null!;
    public string Label { get; private set; } = string.Empty;
    public ParentId ParentId { get; private set; } = null!;

    public ChildEntity(ChildId id, string label, ParentId parentId)
    {
        Id = id;
        Label = label;
        ParentId = parentId;
    }

    protected ChildEntity() { }
}

public sealed class ParentEntity : AggregateRoot<ParentId>
{
    public string Title { get; private set; } = string.Empty;
    public ICollection<ChildEntity> Children { get; private set; } = new List<ChildEntity>();

    public ParentEntity(ParentId id, string title, IClock clock) : base(id, clock)
    {
        Title = title;
    }

    protected ParentEntity() { }
}

// Entity configurations
public sealed class ParentEntityConfig : EntityTypeConfigurationBase<ParentEntity, ParentId>
{
    public override void Configure(EntityTypeBuilder<ParentEntity> builder)
    {
        base.Configure(builder);
        builder.Property(e => e.Title).HasMaxLength(200).IsRequired();
        builder.HasMany(e => e.Children)
            .WithOne()
            .HasForeignKey(c => c.ParentId)
            .IsRequired();
    }
}

public sealed class ChildEntityConfig : IEntityTypeConfiguration<ChildEntity>
{
    public void Configure(EntityTypeBuilder<ChildEntity> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasConversion<Guid>(id => id.Value, v => new ChildId(v));
        builder.Property(e => e.Label).HasMaxLength(200).IsRequired();
        builder.Property(e => e.ParentId).HasConversion<Guid>(id => id.Value, v => new ParentId(v));
    }
}

// DbContext for this test
public sealed class StringIncludeDbContext : SharedKernelDbContext
{
    public DbSet<ParentEntity> Parents => Set<ParentEntity>();
    public DbSet<ChildEntity> Children => Set<ChildEntity>();

    public StringIncludeDbContext(
        DbContextOptions<StringIncludeDbContext> options,
        PersistenceContextDependencies dependencies)
            : base(options, dependencies)
    {
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.ConfigureStronglyTypedId<ParentId, Guid>();
        configurationBuilder.ConfigureStronglyTypedId<ChildId, Guid>();
        base.ConfigureConventions(configurationBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new ParentEntityConfig());
        modelBuilder.ApplyConfiguration(new ChildEntityConfig());
    }
}

// Specifications
internal sealed class AllParentsSpec : Specification<ParentEntity> { }

internal sealed class ParentsWithStringIncludeSpec : Specification<ParentEntity>
{
    public ParentsWithStringIncludeSpec()
    {
        AddStringInclude(nameof(ParentEntity.Children));
    }
}

// Test
public sealed class StringIncludeTests
{
    private static StringIncludeDbContext CreateContext()
    {
        var actorContext = new SharedKernel.Testing.Persistence.FakeAuditActorContext();
        var clock = new SystemClock();

        var options = new DbContextOptionsBuilder<StringIncludeDbContext>()
            .UseSqlite("DataSource=:memory:")
            .ConfigureWarnings(w => w
                .Ignore(RelationalEventId.AmbientTransactionWarning)
                // The rest of this assembly already suppresses this; only this chain did not,
                // so once the other EF failures were fixed this became the test that happened
                // to cross EF's 20-internal-provider threshold and fail in its place.
                .Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;

        var audit = new AuditInterceptor(actorContext, clock);
        var softDelete = new SoftDeleteInterceptor(clock);
        var concurrency = new ConcurrencyInterceptor();

        var ctx = new StringIncludeDbContext(options, new PersistenceContextDependencies(audit, softDelete, concurrency));
        ctx.Database.OpenConnection();
        ctx.Database.EnsureCreated();

        return ctx;
    }

    [Fact]
    public async Task StringInclude_Navigation_Populated_After_Load()
    {
        // Arrange
        using var ctx = CreateContext();
        var parentId = ParentId.New();
        var parent = new ParentEntity(parentId, "Parent1", new SystemClock());
        var child = new ChildEntity(ChildId.New(), "Child1", parentId);

        ctx.Parents.Add(parent);
        ctx.Children.Add(child);
        ctx.SaveChanges();
        ctx.ChangeTracker.Clear();

        var evaluator = new SpecificationEvaluator<ParentEntity>();
        var spec = new ParentsWithStringIncludeSpec();

        // Act
        var query = evaluator.GetQuery(ctx.Parents, spec);
        var results = await query.ToListAsync();

        // Assert
        results.Should().HaveCount(1);
        results[0].Children.Should().HaveCount(1);
        results[0].Children.First().Label.Should().Be("Child1");
    }

    [Fact]
    public async Task StringInclude_EmptyList_DoesNot_Load_Navigation()
    {
        // Arrange
        using var ctx = CreateContext();
        var parentId = ParentId.New();
        var parent = new ParentEntity(parentId, "Parent2", new SystemClock());
        var child = new ChildEntity(ChildId.New(), "Child2", parentId);

        ctx.Parents.Add(parent);
        ctx.Children.Add(child);
        ctx.SaveChanges();
        ctx.ChangeTracker.Clear();

        var evaluator = new SpecificationEvaluator<ParentEntity>();
        var spec = new AllParentsSpec(); // no includes

        // Act
        var query = evaluator.GetQuery(ctx.Parents, spec);
        var results = await query.AsNoTracking().ToListAsync();

        // Assert — children not loaded (no include)
        results.Should().HaveCount(1);
        results[0].Children.Should().BeEmpty();
    }

    [Fact]
    public void AddStringInclude_NullOrWhitespace_Throws_ArgumentException()
    {
        // Arrange
        var spec = new ParentsWithStringIncludeSpec();

        // A direct test of Specification<T> base method
        // We use a derived spec to test the guard
        var act = () =>
        {
            var testSpec = new NullStringIncludeSpec();
        };

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ISpecification_Declares_StringIncludes_Property()
    {
        var property = typeof(SharedKernel.Domain.Specifications.ISpecification<>)
            .GetProperty("StringIncludes");
        property.Should().NotBeNull("ISpecification<T> must declare StringIncludes");
    }
}

// Spec that tries to add null — used only in guard test
file sealed class NullStringIncludeSpec : Specification<ParentEntity>
{
    public NullStringIncludeSpec()
    {
        AddStringInclude(null!); // should throw
    }
}
