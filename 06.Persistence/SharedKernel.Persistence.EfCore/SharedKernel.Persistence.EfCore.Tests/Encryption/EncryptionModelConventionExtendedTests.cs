using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Options;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Domain.Aggregates;
using SharedKernel.Domain.Specifications;
using SharedKernel.Domain.StronglyTypedIds;
using SharedKernel.Persistence.EfCore.Configurations;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Conversions;
using SharedKernel.Persistence.EfCore.Encryption;
using SharedKernel.Persistence.EfCore.Interceptors;
using SharedKernel.Persistence.EfCore.Options;
using SharedKernel.Persistence.EfCore.Repositories;
using SharedKernel.Persistence.EfCore.Specifications;
using SharedKernel.Persistence.EfCore.Tests.TestFixtures;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Persistence.EfCore.Tests.Encryption;

/// <summary>
/// T-44 extended coverage: <see cref="EncryptionModelConvention"/> integration tests —
/// <c>.Encrypt(false)</c> opt-out, disabled options pass-through, multi-property entities,
/// and specification criteria still working when some properties are encrypted.
/// </summary>
public sealed class EncryptionModelConventionExtendedTests
{
    private static IOptionsMonitor<EncryptionOptions> MakeMonitor(EncryptionOptions options)
        => new FixedOptionsMonitor(options);

    private sealed class FixedOptionsMonitor(EncryptionOptions value) : IOptionsMonitor<EncryptionOptions>
    {
        public EncryptionOptions CurrentValue => value;
        public EncryptionOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<EncryptionOptions, string?> listener) => null;
    }

    private static EncryptionOptions EnabledOptions()
    {
        var keyBytes = new byte[32];
        return new EncryptionOptions
        {
            Enabled = true,
            CurrentVersion = "v1",
            Keys = { ["v1"] = Convert.ToBase64String(keyBytes) }
        };
    }

    /// <summary>
    /// Creates a <see cref="MultiPropDbContext"/> using a per-call unique
    /// <see cref="IEncryptionVersionOverride"/> instance so every test gets its own EF Core
    /// model cache entry (keyed by the override reference in
    /// <see cref="SharedKernelDbContext.OnConfiguring"/>).
    /// This prevents one test's <see cref="EncryptedValueConverter"/> — which captures its
    /// <see cref="IOptionsMonitor{TOptions}"/> by reference — from leaking into another test's
    /// cached model via <see cref="EncryptionAwareModelCacheKeyFactory"/>.
    /// </summary>
    private static MultiPropDbContext CreateMultiPropContext(
        IOptionsMonitor<EncryptionOptions>? monitor = null,
        IEncryptionVersionOverride? versionOverride = null)
    {
        var userCtx = TestDbContextFactory.CreateAuthenticatedUserContext(Guid.NewGuid());
        var clock = TestDbContextFactory.CreateClock(DateTimeOffset.UtcNow);
        var svcOpts = TestDbContextFactory.DefaultServiceOptions();

        var audit = new AuditInterceptor(userCtx, clock, svcOpts);
        var softDelete = new SoftDeleteInterceptor(userCtx, clock, svcOpts);
        var concurrency = new ConcurrencyInterceptor();

        var dbOptions = new DbContextOptionsBuilder<MultiPropDbContext>()
            .UseSqlite($"DataSource=file:{Guid.NewGuid():N}?mode=memory&cache=shared")
            .ConfigureWarnings(w =>
                w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;

        // Pass a unique versionOverride instance (or a new NoOp-style override) so the EF Core
        // model cache key (context type + override reference) is unique per test.
        var override_ = versionOverride ?? new EncryptionVersionOverride();
        ISynchronousSymmetricEncryptionService? encryptionService = null;

        if (monitor is not null)
        {
            // Wire up real crypto delegation when options are provided.
            encryptionService = new SynchronousAesGcmEncryptionService(new EncryptionOptionsKeyProvider(monitor, override_));
        }

        return new MultiPropDbContext(
            dbOptions, audit, softDelete, concurrency,
            monitor,
            override_,
            encryptionService);
    }

    // -------------------------------------------------------------------------
    // T-44 (3): .Encrypt(false) — annotation present but disabled → plaintext
    // -------------------------------------------------------------------------

    [Fact]
    public void Property_With_EncryptFalse_HasNoConverter_In_Model()
    {
        var monitor = MakeMonitor(EnabledOptions());
        using var ctx = CreateMultiPropContext(monitor);

        var entityType = ctx.Model.FindEntityType(typeof(MultiPropEntity));
        var optOutProp = entityType?.FindProperty(nameof(MultiPropEntity.OptOutField));

        optOutProp.Should().NotBeNull();
        var converter = optOutProp!.GetValueConverter();
        if (converter is not null)
        {
            converter.Should().NotBeOfType<EncryptedValueConverter>(
                "OptOutField has .Encrypt(false) so EncryptedValueConverter must NOT be applied");
        }
    }

    // -------------------------------------------------------------------------
    // T-44 (4): EncryptionOptions.Enabled == false → annotated property is pass-through
    // -------------------------------------------------------------------------

    [Fact]
    public async Task EncryptionDisabled_AnnotatedProperty_StoredAsPlaintext()
    {
        // Arrange — encryption options disabled
        var disabledOptions = new EncryptionOptions { Enabled = false };
        var monitor = MakeMonitor(disabledOptions);
        using var ctx = CreateMultiPropContext(monitor);
        ctx.Database.EnsureCreated();

        const string email = "plain@example.com";
        var id = MultiPropId.New();
        ctx.Entities.Add(new MultiPropEntity(id, email, "John", "plain-opt-out", new SystemClock()));
        await ctx.SaveChangesAsync();

        // Verify raw stored value is NOT encrypted (no "v" prefix)
        var raw = await ctx.Database
            .SqlQueryRaw<string>("SELECT email FROM multi_prop_entities")
            .ToListAsync();

        raw.Should().ContainSingle()
            .Which.Should().Be(email, "Enabled==false means pass-through — stored as plaintext");
    }

    // -------------------------------------------------------------------------
    // T-44 (5): Multi-property entity — encrypted and non-encrypted properties
    //           behave correctly and independently
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MultiProperty_EncryptedAndPlaintext_BothRoundTripCorrectly()
    {
        var monitor = MakeMonitor(EnabledOptions());
        using var ctx = CreateMultiPropContext(monitor);
        ctx.Database.EnsureCreated();

        const string email = "secure@example.com";
        const string name = "John Doe";
        var id = MultiPropId.New();
        ctx.Entities.Add(new MultiPropEntity(id, email, name, "opt-out", new SystemClock()));
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        // Reload and check EF decryption
        var allEntities = await ctx.Entities.ToListAsync();
        var loaded = allEntities.FirstOrDefault(e => e.Id == id);

        loaded.Should().NotBeNull();
        loaded!.Email.Should().Be(email, "encrypted Email must decrypt back to original");
        loaded.Name.Should().Be(name, "plaintext Name must round-trip unchanged");
    }

    [Fact]
    public async Task MultiProperty_EncryptedEmail_StoredAsCiphertext_PlaintextName_StoredAsPlaintext()
    {
        var monitor = MakeMonitor(EnabledOptions());
        using var ctx = CreateMultiPropContext(monitor);
        ctx.Database.EnsureCreated();

        const string email = "stored@example.com";
        const string name = "PlainName";
        var id = MultiPropId.New();
        ctx.Entities.Add(new MultiPropEntity(id, email, name, "opt-out", new SystemClock()));
        await ctx.SaveChangesAsync();

        // Raw column inspection — email must be encrypted, name must be plaintext
        var rawRows = await ctx.Database
            .SqlQueryRaw<RawMultiProp>("SELECT email, name FROM multi_prop_entities")
            .ToListAsync();

        rawRows.Should().ContainSingle();
        StoredPayload.KeyIdOf(rawRows[0].email).Should().Be("v1",
            "encrypted Email must be stored as an encrypted payload recording its key id");
        rawRows[0].name.Should().Be(name,
            "non-encrypted Name must be stored as-is");
    }

    // -------------------------------------------------------------------------
    // T-44 (6): SpecificationEvaluator criteria on non-encrypted properties
    //           still work when other properties are encrypted
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SpecificationCriteria_OnNonEncryptedProperty_WorksCorrectly_WhenOtherPropertiesEncrypted()
    {
        var monitor = MakeMonitor(EnabledOptions());
        using var ctx = CreateMultiPropContext(monitor);
        ctx.Database.EnsureCreated();

        ctx.Entities.Add(new MultiPropEntity(MultiPropId.New(), "a@example.com", "Alice", "x", new SystemClock()));
        ctx.Entities.Add(new MultiPropEntity(MultiPropId.New(), "b@example.com", "Bob", "y", new SystemClock()));
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        // Query by the non-encrypted Name field
        var repo = new MultiPropReadRepo(ctx);
        var spec = new MultiPropNameEqualsSpec("Alice");
        var results = await repo.ListAsync(spec);

        results.Should().ContainSingle()
            .Which.Name.Should().Be("Alice",
                "criteria filtering on non-encrypted property must work even when Email is encrypted");
    }

    // Projection for raw SQL inspection
    private sealed record RawMultiProp(string email, string name);
}

// ---------------------------------------------------------------------------
// Multi-property test domain types
// ---------------------------------------------------------------------------

internal sealed record MultiPropId(Guid Value) : StronglyTypedId<Guid>(Value)
{
    public static MultiPropId New() => new(Guid.NewGuid());
}

internal sealed class MultiPropEntity : AggregateRoot<MultiPropId>
{
    public string Email { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string OptOutField { get; private set; } = string.Empty;

    public MultiPropEntity(MultiPropId id, string email, string name, string optOutField, IClock clock)
        : base(id, clock)
    {
        Email = email;
        Name = name;
        OptOutField = optOutField;
    }

    protected MultiPropEntity() { } // EF Core path
}

internal sealed class MultiPropEntityConfig : EntityTypeConfigurationBase<MultiPropEntity, MultiPropId>
{
    public override void Configure(EntityTypeBuilder<MultiPropEntity> builder)
    {
        base.Configure(builder);
        builder.ToTable("multi_prop_entities");
        builder.Property(e => e.Email).HasMaxLength(500).Encrypt().IsRequired();
        builder.Property(e => e.Name).HasMaxLength(200).IsRequired(); // NOT encrypted
        builder.Property(e => e.OptOutField).HasMaxLength(200).Encrypt(false).IsRequired(); // opted out
    }
}

internal sealed class MultiPropDbContext : SharedKernelDbContext
{
    public DbSet<MultiPropEntity> Entities => Set<MultiPropEntity>();

    public MultiPropDbContext(
        DbContextOptions<MultiPropDbContext> options,
        AuditInterceptor audit,
        SoftDeleteInterceptor softDelete,
        ConcurrencyInterceptor concurrency,
        IOptionsMonitor<EncryptionOptions>? encryptionOptions = null,
        IEncryptionVersionOverride? encryptionVersionOverride = null,
        ISynchronousSymmetricEncryptionService? symmetricEncryptionService = null)
        : base(options, audit, softDelete, concurrency, null, encryptionOptions,
               encryptionVersionOverride, symmetricEncryptionService)
    {
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.ConfigureStronglyTypedId<MultiPropId, Guid>();
        base.ConfigureConventions(configurationBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new MultiPropEntityConfig());
        // Do NOT call base.OnModelCreating to avoid scanning the whole test assembly.
    }
}

internal sealed class MultiPropReadRepo(MultiPropDbContext ctx)
    : EfReadRepository<MultiPropEntity, MultiPropId>(ctx, new SpecificationEvaluator<MultiPropEntity>());

internal sealed class MultiPropNameEqualsSpec : Specification<MultiPropEntity>
{
    public MultiPropNameEqualsSpec(string name) => AddCriteria(e => e.Name == name);
}
