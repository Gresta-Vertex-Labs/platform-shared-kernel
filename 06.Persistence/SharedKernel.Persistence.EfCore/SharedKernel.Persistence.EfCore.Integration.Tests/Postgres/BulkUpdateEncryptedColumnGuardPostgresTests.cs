using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Npgsql;
using SharedKernel.Domain.Aggregates;
using SharedKernel.Domain.StronglyTypedIds;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.Abstractions.Repositories;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Conversions;
using SharedKernel.Persistence.EfCore.Extensibility;
using SharedKernel.Persistence.EfCore.Repositories;
using SharedKernel.Persistence.EfCore.Extensions;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Testing.Containers;
using SharedKernel.Testing.Persistence;

namespace SharedKernel.Persistence.EfCore.Integration.Tests.Postgres;

/// <summary>
/// Proves <see cref="BulkSpecificationGuard.ValidateSetters{T}"/> rejects a bulk
/// <c>ExecuteUpdateAsync</c> setter that targets a model property annotated with
/// <see cref="PersistenceModelAnnotationNames.Encrypt"/>, against a REAL PostgreSQL table — no
/// plaintext ever reaches the server. Uses the bare annotation directly (the same
/// <c>PersistenceModelAnnotationNames.Encrypt</c> constant <c>SharedKernel.Persistence.EfCore.Encryption</c>'s
/// real <c>.Encrypt(...)</c> extension method sets) rather than referencing that sibling package —
/// this package must never take that dependency.
/// </summary>
[Collection("EfCorePostgres")]
public sealed class BulkUpdateEncryptedColumnGuardPostgresTests
{
    private const string DatabaseName = "sk_p557_bulk_encrypted_guard";

    private readonly PostgreSqlContainerFixture _fixture;

    public BulkUpdateEncryptedColumnGuardPostgresTests(PostgreSqlContainerFixture fixture) => _fixture = fixture;

    private string ConnectionString =>
        new NpgsqlConnectionStringBuilder(_fixture.ConnectionString) { Database = DatabaseName }.ConnectionString;

    private EncryptedFieldTestDbContext CreateContext()
    {
        var builder = new DbContextOptionsBuilder<EncryptedFieldTestDbContext>();
        builder.UsePostgres(TestNpgsqlDataSources.Get(ConnectionString));
        var options = builder.Options;
        var actor = new FakeAuditActorContext("bulk-encrypted-guard-test");

        return new EncryptedFieldTestDbContext(
            options,
            PersistenceContextDependencies.Create(actor, new SystemClock()));
    }

    [Fact]
    public async Task ExecuteUpdateAsync_TargetingAnEncryptedProperty_ThrowsBeforeAnySqlRuns_AndLeavesTheRowUntouched()
    {
        await using var setup = CreateContext();
        await setup.Database.EnsureCreatedAsync();

        var id = EncryptedFieldId.New();
        await using (var seedCtx = CreateContext())
        {
            seedCtx.Items.Add(new EncryptedFieldAggregate(id, "original-plaintext-value"));
            await seedCtx.SaveChangesAsync();
        }

        await using var ctx = CreateContext();
        var repository = new EncryptedFieldRepository(ctx);

        var act = () => repository.ExecuteUpdateAsync(
            new AllRowsSpecification<EncryptedFieldAggregate>(),
            s => s.SetProperty(x => x.SecretValue, "attacker-supplied-plaintext"));

        await act.Should().ThrowAsync<UnsupportedSpecificationException>(
            "a bulk ExecuteUpdate must never write directly into a column the model marks encrypted — " +
                "it bypasses the encryption save-changes interceptor entirely");

        await using var verifyCtx = CreateContext();
        var reloaded = await verifyCtx.Items.SingleAsync(x => x.Id == id);
        reloaded.SecretValue.Should().Be(
            "original-plaintext-value", "the row must be completely untouched — the guard must fire " +
                "BEFORE any SQL reaches the server, not merely roll back after");
    }

    [Fact]
    public async Task ExecuteUpdateAsync_TargetingAnUnencryptedProperty_StillSucceeds()
    {
        // Control: the guard must not become overzealous and reject every bulk update on an entity
        // that merely HAS an encrypted property — only a setter that actually TARGETS it.
        await using var setup = CreateContext();
        await setup.Database.EnsureCreatedAsync();
        await setup.Items.ExecuteDeleteAsync(); // both tests share one database; count only this test's row

        var id = EncryptedFieldId.New();
        await using (var seedCtx = CreateContext())
        {
            seedCtx.Items.Add(new EncryptedFieldAggregate(id, "original-plaintext-value"));
            await seedCtx.SaveChangesAsync();
        }

        await using var ctx = CreateContext();
        var repository = new EncryptedFieldRepository(ctx);

        var updated = await repository.ExecuteUpdateAsync(
            new AllRowsSpecification<EncryptedFieldAggregate>(),
            s => s.SetProperty(x => x.PlainLabel, "new-plain-label"));

        updated.Should().Be(1);
    }
}

// ---------------------------------------------------------------------------
// Minimal fixtures.
// ---------------------------------------------------------------------------

public sealed record EncryptedFieldId(Guid Value) : StronglyTypedId<Guid>(Value)
{
    public static EncryptedFieldId New() => new(Guid.NewGuid());
}

public sealed class EncryptedFieldAggregate : AggregateRoot<EncryptedFieldId>
{
    public string SecretValue { get; private set; } = string.Empty;
    public string PlainLabel { get; private set; } = string.Empty;

    public EncryptedFieldAggregate(EncryptedFieldId id, string secretValue) : base(id, new SystemClock())
    {
        SecretValue = secretValue;
        PlainLabel = "unset";
    }

    private EncryptedFieldAggregate() { } // ORM path
}

public sealed class EncryptedFieldAggregateConfig : IEntityTypeConfiguration<EncryptedFieldAggregate>
{
    public void Configure(EntityTypeBuilder<EncryptedFieldAggregate> builder)
    {
        builder.HasKey("Id");
        builder.ToTable("bulk_encrypted_guard_item");

        // The same bare annotations SharedKernel.Persistence.EfCore.Encryption's real .Encrypt(...)
        // extension method (plus its EncryptionModelConvention) sets — applied directly here so this
        // test needs no reference to that sibling package. EncryptApplied must be set too, or this
        // fixture trips EncryptAnnotationRegisteredGuardConvention's OWN "encryption never wired"
        // guard at model-build time — a different check than the one this suite exists to prove.
        builder.Property(x => x.SecretValue)
            .HasAnnotation(PersistenceModelAnnotationNames.Encrypt, "test.bulk-guard-secret")
                .HasAnnotation(PersistenceModelAnnotationNames.EncryptApplied, true);

        builder.Property(x => x.PlainLabel).HasMaxLength(200).IsRequired();
    }
}

public sealed class EncryptedFieldTestDbContext : SharedKernelDbContext
{
    public DbSet<EncryptedFieldAggregate> Items => Set<EncryptedFieldAggregate>();

    public EncryptedFieldTestDbContext(DbContextOptions<EncryptedFieldTestDbContext> options, PersistenceContextDependencies dependencies)
        : base(options, dependencies)
    {
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfiguration(new EncryptedFieldAggregateConfig());
}

public sealed class EncryptedFieldRepository(SharedKernelDbContext dbContext)
    : EfRepository<EncryptedFieldAggregate, EncryptedFieldId>(dbContext);
