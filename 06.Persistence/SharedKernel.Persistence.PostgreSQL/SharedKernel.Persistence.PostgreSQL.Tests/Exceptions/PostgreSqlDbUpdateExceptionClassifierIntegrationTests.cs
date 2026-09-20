using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SharedKernel.Core.Exceptions;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Extensibility;
using SharedKernel.Persistence.EfCore.Interceptors;
using SharedKernel.Persistence.PostgreSQL.Exceptions;
using SharedKernel.Persistence.PostgreSQL.Extensions;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Containers;
using SharedKernel.Testing.Persistence;

namespace SharedKernel.Persistence.PostgreSQL.Tests.Exceptions;

// ---------------------------------------------------------------------------
// Minimal schema for real unique/FK constraint violations.
// ---------------------------------------------------------------------------

public sealed class ClassifierParent
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
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
        builder.UsePostgreSQL(_fixture.ConnectionString);
        var options = builder.Options;

        var actorContext = new FakeAuditActorContext();
        var clock = new FakeClock();
        var audit = new AuditInterceptor(actorContext, clock);
        var softDelete = new SoftDeleteInterceptor(actorContext, clock);
        var concurrency = new ConcurrencyInterceptor();
        var classifiers = new IDbUpdateExceptionClassifier[] { new PostgreSqlDbUpdateExceptionClassifier() };

        var ctx = new ClassifierTestDbContext(options, new PersistenceContextDependencies(audit, softDelete, concurrency, exceptionClassifiers: classifiers));
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
}
