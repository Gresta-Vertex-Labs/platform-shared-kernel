using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Extensibility;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Testing.Persistence;

namespace SharedKernel.Persistence.EfCore.Tests.Conventions;

/// <summary>
/// <see cref="Persistence.EfCore.Conventions.EncryptAnnotationRegisteredGuardConvention"/>: model
/// building must fail loudly for a property annotated <c>.Encrypt(...)</c> but never marked
/// <see cref="PersistenceModelAnnotationNames.EncryptApplied"/> — whether that property is a direct
/// entity property OR reachable only through an EF Core complex-type sub-property.
/// </summary>
/// <remarks>
/// Four DISTINCT <see cref="DbContext"/> types, one per scenario — never one type toggled by an
/// instance-level flag. EF Core's default model cache key is derived from the CONTEXT TYPE (plus
/// provider options), not from any per-instance state captured inside <c>OnModelCreating</c>; two
/// instances of the SAME context type sharing a process would silently share the FIRST one's built
/// model, making a "same type, different flag" design prove nothing (confirmed by writing it that way
/// first — every "should throw" case failed because it received the OTHER instance's cached, throw-free
/// model).
/// </remarks>
public sealed class EncryptAnnotationRegisteredGuardConventionTests
{
    // ---------------------------------------------------------------------------
    // Shared fixtures
    // ---------------------------------------------------------------------------

    private sealed class DirectPropEntity
    {
        public int Id { get; set; }
        public string Secret { get; set; } = string.Empty;
    }

    private sealed class NoteBox
    {
        public string Body { get; set; } = string.Empty;
    }

    private sealed class ComplexPropEntity
    {
        public int Id { get; set; }
        public NoteBox Note { get; set; } = new();
    }

    private static PersistenceContextDependencies BuildDependencies()
    {
        var actor = new FakeAuditActorContext("encrypt-guard-test");
        var clock = new SystemClock();
        return PersistenceContextDependencies.Create(actor, clock);
    }

    private static DbContextOptions<TContext> BuildSqliteOptions<TContext>(SqliteConnection connection)
        where TContext : DbContext
    {
        var builder = new DbContextOptionsBuilder<TContext>();
        builder.UseSqlite(connection)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning));
        return builder.Options;
    }

    // ---------------------------------------------------------------------------
    // Direct property, never marked applied — must throw
    // ---------------------------------------------------------------------------

    private sealed class DirectPropNotAppliedDbContext(
        DbContextOptions<DirectPropNotAppliedDbContext> options, PersistenceContextDependencies dependencies)
            : SharedKernelDbContext(options, dependencies)
    {
        public DbSet<DirectPropEntity> Items => Set<DirectPropEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<DirectPropEntity>(e =>
            {
                e.HasKey(x => x.Id);
                e.Property(x => x.Secret).HasAnnotation(PersistenceModelAnnotationNames.Encrypt, "test.direct-purpose");
            });
    }

    [Fact]
    public void ProcessModelFinalizing_DirectPropertyEncryptedButNeverApplied_Throws()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var ctx = new DirectPropNotAppliedDbContext(
            BuildSqliteOptions<DirectPropNotAppliedDbContext>(connection), BuildDependencies());

        var act = () => ctx.Model;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*DirectPropEntity.Secret*")
                .Which.Message.Should().Contain("WithEncryption");
    }

    // ---------------------------------------------------------------------------
    // Direct property, marked applied (simulating EncryptionModelConvention having run) — must not throw
    // ---------------------------------------------------------------------------

    private sealed class DirectPropAppliedDbContext(
        DbContextOptions<DirectPropAppliedDbContext> options, PersistenceContextDependencies dependencies)
            : SharedKernelDbContext(options, dependencies)
    {
        public DbSet<DirectPropEntity> Items => Set<DirectPropEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<DirectPropEntity>(e =>
            {
                e.HasKey(x => x.Id);
                var property = e.Property(x => x.Secret);
                property.HasAnnotation(PersistenceModelAnnotationNames.Encrypt, "test.direct-purpose");
                property.HasAnnotation(PersistenceModelAnnotationNames.EncryptApplied, true);
            });
    }

    [Fact]
    public void ProcessModelFinalizing_DirectPropertyEncryptedAndApplied_DoesNotThrow()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var ctx = new DirectPropAppliedDbContext(
            BuildSqliteOptions<DirectPropAppliedDbContext>(connection), BuildDependencies());

        var act = () => ctx.Model;

        act.Should().NotThrow();
    }

    // ---------------------------------------------------------------------------
    // Complex-type sub-property, never marked applied — the fix this suite exists to prove (C4)
    // ---------------------------------------------------------------------------

    private sealed class ComplexPropNotAppliedDbContext(
        DbContextOptions<ComplexPropNotAppliedDbContext> options, PersistenceContextDependencies dependencies)
            : SharedKernelDbContext(options, dependencies)
    {
        public DbSet<ComplexPropEntity> Items => Set<ComplexPropEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<ComplexPropEntity>(e =>
            {
                e.HasKey(x => x.Id);
                e.ComplexProperty(x => x.Note, cp =>
                    cp.Property(n => n.Body).HasAnnotation(PersistenceModelAnnotationNames.Encrypt, "test.complex-purpose"));
            });
    }

    [Fact]
    public void ProcessModelFinalizing_ComplexSubPropertyEncryptedButNeverApplied_Throws()
    {
        // Before the fix, GetComplexProperties() was never walked here, so a model whose ONLY
        // encrypted property lives inside a complex type passed this guard silently — exactly the
        // fail-open gap the guard exists to close, one level deeper.
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var ctx = new ComplexPropNotAppliedDbContext(
            BuildSqliteOptions<ComplexPropNotAppliedDbContext>(connection), BuildDependencies());

        var act = () => ctx.Model;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ComplexPropEntity.Note.Body*")
                .Which.Message.Should().Contain("WithEncryption");
    }

    // ---------------------------------------------------------------------------
    // Complex-type sub-property, marked applied — must not throw
    // ---------------------------------------------------------------------------

    private sealed class ComplexPropAppliedDbContext(
        DbContextOptions<ComplexPropAppliedDbContext> options, PersistenceContextDependencies dependencies)
            : SharedKernelDbContext(options, dependencies)
    {
        public DbSet<ComplexPropEntity> Items => Set<ComplexPropEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<ComplexPropEntity>(e =>
            {
                e.HasKey(x => x.Id);
                e.ComplexProperty(x => x.Note, cp =>
                {
                    var property = cp.Property(n => n.Body);
                    property.HasAnnotation(PersistenceModelAnnotationNames.Encrypt, "test.complex-purpose");
                    property.HasAnnotation(PersistenceModelAnnotationNames.EncryptApplied, true);
                });
            });
    }

    [Fact]
    public void ProcessModelFinalizing_ComplexSubPropertyEncryptedAndApplied_DoesNotThrow()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var ctx = new ComplexPropAppliedDbContext(
            BuildSqliteOptions<ComplexPropAppliedDbContext>(connection), BuildDependencies());

        var act = () => ctx.Model;

        act.Should().NotThrow();
    }

    // ---------------------------------------------------------------------------
    // Baseline: no .Encrypt(...) anywhere — must never throw
    // ---------------------------------------------------------------------------

    private sealed class NoEncryptionDbContext(
        DbContextOptions<NoEncryptionDbContext> options, PersistenceContextDependencies dependencies)
            : SharedKernelDbContext(options, dependencies)
    {
        public DbSet<DirectPropEntity> Items => Set<DirectPropEntity>();
        public DbSet<ComplexPropEntity> ComplexItems => Set<ComplexPropEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<DirectPropEntity>(e => e.HasKey(x => x.Id));
            modelBuilder.Entity<ComplexPropEntity>(e =>
            {
                e.HasKey(x => x.Id);
                e.ComplexProperty(x => x.Note);
            });
        }
    }

    [Fact]
    public void ProcessModelFinalizing_NoEncryptAnnotationAnywhere_DoesNotThrow()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var ctx = new NoEncryptionDbContext(BuildSqliteOptions<NoEncryptionDbContext>(connection), BuildDependencies());

        var act = () => ctx.Model;

        act.Should().NotThrow();
    }
}
