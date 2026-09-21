using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Core.Exceptions;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Extensibility;
using SharedKernel.Persistence.EfCore.Exceptions;
using SharedKernel.Persistence.EfCore.Extensions;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Containers;
using SharedKernel.Testing.Persistence;

namespace SharedKernel.Persistence.EfCore.Integration.Tests.PostgreSql.Exceptions;

// ---------------------------------------------------------------------------
// Minimal schema for real unique/FK constraint violations.
// ---------------------------------------------------------------------------

public sealed class ClassifierParent
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public string Label { get; set; } = string.Empty;
}

public sealed class ClassifierChild
{
    public int Id { get; set; }
    public int ParentId { get; set; }
}

public sealed class ClassifierParentConfig : IEntityTypeConfiguration<ClassifierParent>
{
    public void Configure(EntityTypeBuilder<ClassifierParent> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.HasIndex(e => e.Code).IsUnique();
        builder.Property(e => e.Label).HasMaxLength(5);
        builder.ToTable(t => t.HasCheckConstraint("ck_classifier_parent_quantity", "quantity >= 0"));
    }
}

public sealed class ClassifierChildConfig : IEntityTypeConfiguration<ClassifierChild>
{
    public void Configure(EntityTypeBuilder<ClassifierChild> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.HasOne<ClassifierParent>().WithMany().HasForeignKey(e => e.ParentId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ClassifierTestDbContext : SharedKernelDbContext
{
    public DbSet<ClassifierParent> Parents => Set<ClassifierParent>();

    public DbSet<ClassifierChild> Children => Set<ClassifierChild>();

    public ClassifierTestDbContext(
        DbContextOptions<ClassifierTestDbContext> options,
        PersistenceContextDependencies dependencies)
            : base(options, dependencies)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new ClassifierParentConfig());
        modelBuilder.ApplyConfiguration(new ClassifierChildConfig());
    }
}

/// <summary>
/// <see cref="PostgreSqlDbUpdateExceptionClassifier"/> against real PostgreSQL constraint
/// violations: a unique-index violation becomes <see cref="ConflictException"/>, an insert/update
/// foreign-key violation becomes <see cref="ValidationException"/>, and a delete blocked by a
/// dependent row becomes <see cref="ConflictException"/>.
/// </summary>
public sealed class PostgreSqlDbUpdateExceptionClassifierIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainerFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private async Task<ClassifierTestDbContext> CreateContextAsync()
    {
        var builder = new DbContextOptionsBuilder<ClassifierTestDbContext>();
        builder.UsePostgreSQL(TestNpgsqlDataSources.Get(_fixture.ConnectionString));
        var options = builder.Options;

        var actorContext = new FakeAuditActorContext();
        var clock = new FakeClock();
        var audit = PersistenceContextDependencies.Create(actorContext, clock);
        var classifiers = new IDbUpdateExceptionClassifier[] { new PostgreSqlDbUpdateExceptionClassifier() };

        var ctx = new ClassifierTestDbContext(options, audit);
        await ctx.Database.EnsureCreatedAsync();
        return ctx;
    }

    [Fact]
    public async Task SaveChanges_UniqueViolation_ThrowsConflictException()
    {
        await using var ctx = await CreateContextAsync();

        ctx.Parents.Add(new ClassifierParent { Code = "DUP" });
        await ctx.SaveChangesAsync();

        ctx.Parents.Add(new ClassifierParent { Code = "DUP" });

        var act = async () => await ctx.SaveChangesAsync();

        (await act.Should().ThrowAsync<ConflictException>())
            .Which.Error.Code.Should().Be("persistence.postgresql.unique_violation");
    }

    [Fact]
    public async Task SaveChanges_ForeignKeyViolationOnInsert_ThrowsValidationException()
    {
        await using var ctx = await CreateContextAsync();

        ctx.Children.Add(new ClassifierChild { ParentId = 999_999 });

        var act = async () => await ctx.SaveChangesAsync();

        (await act.Should().ThrowAsync<ValidationException>())
            .Which.Errors[0].Code.Should().Be("persistence.postgresql.foreign_key_reference_missing");
    }

    [Fact]
    public async Task SaveChanges_ForeignKeyViolationOnDelete_ThrowsConflictException()
    {
        int parentId;

        await using (var seedCtx = await CreateContextAsync())
        {
            var parent = new ClassifierParent { Code = "P1" };
            seedCtx.Parents.Add(parent);
            await seedCtx.SaveChangesAsync();

            seedCtx.Children.Add(new ClassifierChild { ParentId = parent.Id });
            await seedCtx.SaveChangesAsync();

            parentId = parent.Id;
        }

        // A FRESH context, with no local knowledge of the still-referencing child, is required —
        // otherwise EF Core's own client-side "severed required relationship" guard throws
        // InvalidOperationException from DetectChanges BEFORE any SQL is even sent, never
        // reaching the real server-side FK violation this test proves the classifier handles.
        await using var ctx = await CreateContextAsync();
        var parentToDelete = await ctx.Parents.FindAsync(parentId);
        ctx.Parents.Remove(parentToDelete!);

        var act = async () => await ctx.SaveChangesAsync();

        (await act.Should().ThrowAsync<ConflictException>())
            .Which.Error.Code.Should().Be("persistence.postgresql.foreign_key_dependent_exists");
    }

    [Fact]
    public async Task SaveChanges_ClassifiedException_KeepsTheDbUpdateExceptionAsInner()
    {
        await using var ctx = await CreateContextAsync();

        ctx.Parents.Add(new ClassifierParent { Code = "INNER" });
        await ctx.SaveChangesAsync();
        ctx.Parents.Add(new ClassifierParent { Code = "INNER" });

        var act = async () => await ctx.SaveChangesAsync();

        (await act.Should().ThrowAsync<ConflictException>())
            .Which.InnerException.Should().BeOfType<DbUpdateException>()
            .Which.InnerException.Should().BeOfType<global::Npgsql.PostgresException>();
    }

    [Fact]
    public async Task SaveChanges_ForeignKeyViolation_MixedBatch_ClassifiesByTheFailingInsert_NotByAnUnrelatedDelete()
    {
        // A batch that deletes an unrelated, unreferenced parent AND inserts an orphan child: the old
        // "any Deleted entry means dependent exists" rule misread this as a Conflict.
        int unrelatedParentId;
        await using (var seedCtx = await CreateContextAsync())
        {
            var unrelated = new ClassifierParent { Code = "UNREL" };
            seedCtx.Parents.Add(unrelated);
            await seedCtx.SaveChangesAsync();
            unrelatedParentId = unrelated.Id;
        }

        await using var ctx = await CreateContextAsync();
        ctx.Parents.Remove((await ctx.Parents.FindAsync(unrelatedParentId))!);
        ctx.Children.Add(new ClassifierChild { ParentId = 888_888 });

        var act = async () => await ctx.SaveChangesAsync();

        (await act.Should().ThrowAsync<ValidationException>())
            .Which.Errors[0].Code.Should().Be("persistence.postgresql.foreign_key_reference_missing");
    }

    [Fact]
    public async Task SaveChanges_CheckViolation_ThrowsValidationException()
    {
        await using var ctx = await CreateContextAsync();
        ctx.Parents.Add(new ClassifierParent { Code = "CHK", Quantity = -1 });

        var act = async () => await ctx.SaveChangesAsync();

        (await act.Should().ThrowAsync<ValidationException>())
            .Which.Errors[0].Code.Should().Be("persistence.postgresql.check_violation");
    }

    [Fact]
    public async Task SaveChanges_ValueTooLong_ThrowsValidationException()
    {
        await using var ctx = await CreateContextAsync();
        ctx.Parents.Add(new ClassifierParent { Code = "LONG", Label = "longer-than-five" });

        var act = async () => await ctx.SaveChangesAsync();

        (await act.Should().ThrowAsync<ValidationException>())
            .Which.Errors[0].Code.Should().Be("persistence.postgresql.value_too_long");
    }

    [Fact]
    public async Task SaveChanges_ThroughDiRegistration_ClassifierIsAlwaysRegistered()
    {
        // No explicit classifier anywhere: AddSharedKernelEfCore(...).Build() registers it itself.
        var services = new ServiceCollection();
        services
            .AddSharedKernelEfCore<ClassifierTestDbContext>(o =>
                o.UsePostgreSQL(TestNpgsqlDataSources.Get(_fixture.ConnectionString)))
            .Build();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<ClassifierTestDbContext>();
        await ctx.Database.EnsureCreatedAsync();

        ctx.Parents.Add(new ClassifierParent { Code = "DI-DUP" });
        await ctx.SaveChangesAsync();
        ctx.Parents.Add(new ClassifierParent { Code = "DI-DUP" });

        var act = async () => await ctx.SaveChangesAsync();

        (await act.Should().ThrowAsync<ConflictException>())
            .Which.Error.Code.Should().Be("persistence.postgresql.unique_violation");
    }
}
