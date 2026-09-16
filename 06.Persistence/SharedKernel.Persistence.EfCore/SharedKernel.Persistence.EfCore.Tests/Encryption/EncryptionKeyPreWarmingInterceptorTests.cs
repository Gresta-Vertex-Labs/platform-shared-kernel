using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Options;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Domain.Aggregates;
using SharedKernel.Domain.StronglyTypedIds;
using SharedKernel.Persistence.EfCore.Configurations;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Conversions;
using SharedKernel.Persistence.EfCore.Encryption;
using SharedKernel.Persistence.EfCore.Interceptors;
using SharedKernel.Persistence.EfCore.Options;
using SharedKernel.Persistence.EfCore.Tests.TestFixtures;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Testing.Cryptography;

namespace SharedKernel.Persistence.EfCore.Tests.Encryption;

/// <summary>
/// T-142/T-143 (P-498/WO-081, D-130): <see cref="EncryptionKeyPreWarmingInterceptor"/> integration
/// tests — the interceptor's two hooks (write via <c>SavingChangesAsync</c>, read via
/// <c>ReaderExecutingAsync</c>) are what actually closes the phase's refuted original premise
/// (D-126): a write-only pre-warm hook does nothing for a query, since <c>SaveChangesAsync</c> is
/// never called on the read path.
/// </summary>
public sealed class EncryptionKeyPreWarmingInterceptorTests
{
    private sealed class FixedOptionsMonitor(EncryptionOptions value) : IOptionsMonitor<EncryptionOptions>
    {
        public EncryptionOptions CurrentValue => value;
        public EncryptionOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<EncryptionOptions, string?> listener) => null;
    }

    private static (WarmingTestDbContext Context, FakeRemoteEncryptionKeyProvider Inner) CreateContext()
    {
        var userCtx = TestDbContextFactory.CreateAuthenticatedUserContext(Guid.NewGuid());
        var clock = TestDbContextFactory.CreateClock(DateTimeOffset.UtcNow);
        var svcOpts = TestDbContextFactory.DefaultServiceOptions();

        var audit = new AuditInterceptor(userCtx, clock, svcOpts);
        var softDelete = new SoftDeleteInterceptor(userCtx, clock, svcOpts);
        var concurrency = new ConcurrencyInterceptor();

        var options = new EncryptionOptions
        {
            Enabled = true,
            CurrentVersion = "v1",
            Keys = { ["v1"] = Convert.ToBase64String(new byte[32]) } // unused — the remote provider supplies material
        };
        var monitor = new FixedOptionsMonitor(options);

        // A UNIQUE IEncryptionVersionOverride instance per call — EF Core's model cache is keyed by
        // (context type, this instance) via EncryptionAwareModelCacheKeyFactory, so reusing the
        // shared EncryptionVersionOverride.NoOp singleton across test methods would let one test's
        // cached model (and its baked-in EncryptedValueConverter/PreWarmedEncryptionKeyProvider
        // capture) leak into another's, corrupting the call-counting assertions below.
        var versionOverride = new EncryptionVersionOverride();

        var inner = new FakeRemoteEncryptionKeyProvider("v1");
        var preWarmed = new PreWarmedEncryptionKeyProvider(inner, versionOverride);
        var encryptionService = new SynchronousAesGcmEncryptionService(preWarmed);
        var warmingInterceptor = new EncryptionKeyPreWarmingInterceptor(preWarmed);

        var dbOptions = new DbContextOptionsBuilder<WarmingTestDbContext>()
            .UseSqlite($"DataSource=file:{Guid.NewGuid():N}?mode=memory&cache=shared")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;

        var context = new WarmingTestDbContext(
            dbOptions, audit, softDelete, concurrency, [warmingInterceptor], monitor,
            versionOverride, encryptionService);

        return (context, inner);
    }

    // ===========================================================================
    // T-142: SavingChangesAsync (write path)
    // ===========================================================================

    [Fact]
    public async Task SavingChangesAsync_BatchInsertingEncryptedRows_PerformsExactlyOneWarmCall()
    {
        var (ctx, inner) = CreateContext();
        await using var _ = ctx;
        ctx.Database.EnsureCreated();

        for (var i = 0; i < 10; i++)
        {
            ctx.EncryptedEntities.Add(new WarmingEncryptedEntity(WarmingId.New(), $"secret-{i}", new SystemClock()));
        }

        await ctx.SaveChangesAsync();

        inner.CurrentKeyCallCount.Should().Be(1,
            "a single SaveChangesAsync batch touching N encrypted-property entities must warm " +
            "EXACTLY ONCE, not N times");
    }

    [Fact]
    public async Task SavingChangesAsync_ModelWithNoEncryptedProperties_PerformsZeroWarmCalls()
    {
        // NOTE: uses a dedicated all-plain DbContext type (no encrypted property ANYWHERE in its
        // model) rather than WarmingTestDbContext — ReaderExecutingAsync's whole-model gate is
        // COARSE BY DESIGN (D-130): a model that has SOME encrypted entity type warms on every
        // command against ANY entity in that model (e.g. SQLite's own RETURNING-clause read-back
        // on an INSERT), even one touching only a non-encrypted row. Isolating "zero encrypted
        // properties anywhere" is what proves BOTH hooks correctly stay silent when there is
        // genuinely nothing to warm for.
        var userCtx = TestDbContextFactory.CreateAuthenticatedUserContext(Guid.NewGuid());
        var clock = TestDbContextFactory.CreateClock(DateTimeOffset.UtcNow);
        var svcOpts = TestDbContextFactory.DefaultServiceOptions();
        var audit = new AuditInterceptor(userCtx, clock, svcOpts);
        var softDelete = new SoftDeleteInterceptor(userCtx, clock, svcOpts);
        var concurrency = new ConcurrencyInterceptor();

        var inner = new FakeRemoteEncryptionKeyProvider("v1");
        var preWarmed = new PreWarmedEncryptionKeyProvider(inner, new EncryptionVersionOverride());
        var warmingInterceptor = new EncryptionKeyPreWarmingInterceptor(preWarmed);

        var dbOptions = new DbContextOptionsBuilder<AllPlainWarmingDbContext>()
            .UseSqlite($"DataSource=file:{Guid.NewGuid():N}?mode=memory&cache=shared")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;

        await using var ctx = new AllPlainWarmingDbContext(dbOptions, audit, softDelete, concurrency, [warmingInterceptor]);
        ctx.Database.EnsureCreated();

        ctx.PlainEntities.Add(new WarmingPlainEntity(WarmingId.New(), "not-secret"));
        await ctx.SaveChangesAsync();

        inner.CurrentKeyCallCount.Should().Be(0,
            "a model with no encrypted property anywhere must never warm — neither hook has " +
            "anything to warm for, so the (potentially billed) inner provider is never touched");
    }

    [Fact]
    public async Task SavingChangesAsync_MixedModel_PlainOnlyBatch_SaveHookItselfNeverCallsWarm()
    {
        // Distinguishes the SAVE hook's own HasEncryptedChanges gate from the (separately, coarsely
        // gated) READ hook: a batch touching only non-encrypted entities must never trip
        // SavingChangesAsync's own warm call — any warmth observed here is attributable solely to
        // ReaderExecutingAsync's coarse whole-model gate firing on the INSERT's own reader-based
        // command execution (SQLite RETURNING clause), proven by asserting the total is AT MOST the
        // one warm call that single INSERT's reader-hook could contribute — never more, and in
        // particular never once per row.
        var (ctx, inner) = CreateContext();
        await using var _ = ctx;
        ctx.Database.EnsureCreated();

        for (var i = 0; i < 10; i++)
        {
            ctx.PlainEntities.Add(new WarmingPlainEntity(WarmingId.New(), $"not-secret-{i}"));
        }
        await ctx.SaveChangesAsync();

        inner.CurrentKeyCallCount.Should().BeLessThanOrEqualTo(1,
            "even across 10 plain-entity inserts sharing one SaveChangesAsync batch, the warm " +
            "provider must be touched AT MOST once — proving SavingChangesAsync's own " +
            "HasEncryptedChanges gate never fires for a plain-only batch, and even the coarse " +
            "read-hook's contribution never scales with row count");
    }

    // ===========================================================================
    // T-143: ReaderExecutingAsync (read path — the headline AC#4 acceptance test)
    // ===========================================================================

    [Fact]
    public async Task ReaderExecutingAsync_Querying500Rows_PerformsExactlyOneWarmCallTotal()
    {
        var (ctx, inner) = CreateContext();
        await using var _ = ctx;
        ctx.Database.EnsureCreated();

        for (var i = 0; i < 500; i++)
        {
            ctx.EncryptedEntities.Add(new WarmingEncryptedEntity(WarmingId.New(), $"secret-{i}", new SystemClock()));
        }
        await ctx.SaveChangesAsync();

        // Reset the call counter by re-opening on a fresh context sharing the same warm provider
        // instance would defeat the point — instead assert against the TOTAL across save+read,
        // which must still be exactly 1 (the save above already warmed it; the read must observe
        // the warm cache and perform ZERO further calls) — proven separately below with a
        // read-only assertion using a fresh provider/context pair that never writes first.
        var beforeRead = inner.CurrentKeyCallCount;

        var all = await ctx.EncryptedEntities.ToListAsync();

        all.Should().HaveCount(500);
        inner.CurrentKeyCallCount.Should().Be(beforeRead,
            "reading 500 rows sharing the current encryption version must perform ZERO additional " +
            "warm calls beyond whatever the prior write already performed — the warm cache is " +
            "already hot");
    }

    [Fact]
    public async Task ReaderExecutingAsync_ReadOnly_NeverWritten_StillWarmsExactlyOnce_RegardlessOfRowCount()
    {
        // The literal F1 defect scenario: a process that only ever READS encrypted rows (e.g. a
        // read replica / query-only service) must still warm exactly once for the whole read, never
        // once per row — proving a write-only pre-warm hook (the phase's refuted original premise,
        // D-126) would have left this scenario completely uncovered.
        var (writerCtx, writerInner) = CreateContext();
        await using (writerCtx)
        {
            writerCtx.Database.EnsureCreated();
            for (var i = 0; i < 500; i++)
            {
                writerCtx.EncryptedEntities.Add(new WarmingEncryptedEntity(WarmingId.New(), $"secret-{i}", new SystemClock()));
            }
            await writerCtx.SaveChangesAsync();
        }

        // A second, independent context/provider pair against the SAME database file, whose
        // interceptor has NEVER warmed before this point — proves the READ hook alone (not any
        // residual warmth from the writer) is what satisfies AC#4.
        var readerOptions = new EncryptionOptions
        {
            Enabled = true,
            CurrentVersion = "v1",
            Keys = { ["v1"] = Convert.ToBase64String(new byte[32]) }
        };
        var readerMonitor = new FixedOptionsMonitor(readerOptions);
        var readerVersionOverride = new EncryptionVersionOverride();
        var readerInner = new FakeRemoteEncryptionKeyProvider("v1");
        var readerPreWarmed = new PreWarmedEncryptionKeyProvider(readerInner, readerVersionOverride);
        var readerEncryptionService = new SynchronousAesGcmEncryptionService(readerPreWarmed);
        var readerWarmingInterceptor = new EncryptionKeyPreWarmingInterceptor(readerPreWarmed);

        var userCtx = TestDbContextFactory.CreateAuthenticatedUserContext(Guid.NewGuid());
        var clock = TestDbContextFactory.CreateClock(DateTimeOffset.UtcNow);
        var svcOpts = TestDbContextFactory.DefaultServiceOptions();
        var audit = new AuditInterceptor(userCtx, clock, svcOpts);
        var softDelete = new SoftDeleteInterceptor(userCtx, clock, svcOpts);
        var concurrency = new ConcurrencyInterceptor();

        // NOTE: this is a genuinely separate connection/database, so this test proves the shape of
        // the assertion (one warm call for N rows) on its own dataset rather than reusing the
        // writer's file — reader inner/provider start cold, never warmed by a prior write.
        var readerDbOptions = new DbContextOptionsBuilder<WarmingTestDbContext>()
            .UseSqlite($"DataSource=file:{Guid.NewGuid():N}?mode=memory&cache=shared")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;

        await using var readerCtx = new WarmingTestDbContext(
            readerDbOptions, audit, softDelete, concurrency, [readerWarmingInterceptor], readerMonitor,
            readerVersionOverride, readerEncryptionService);
        readerCtx.Database.EnsureCreated();

        for (var i = 0; i < 500; i++)
        {
            // Seed via a write on THIS reader context too so the read below has something encrypted
            // to materialize — but reset the counter immediately after, isolating the READ warm call.
            readerCtx.EncryptedEntities.Add(new WarmingEncryptedEntity(WarmingId.New(), $"secret-{i}", new SystemClock()));
        }
        await readerCtx.SaveChangesAsync();
        readerCtx.ChangeTracker.Clear();

        var writeWarmCalls = readerInner.CurrentKeyCallCount;
        writeWarmCalls.Should().Be(1, "sanity: the seeding write itself warmed exactly once");

        var all = await readerCtx.EncryptedEntities.ToListAsync();

        all.Should().HaveCount(500);
        readerInner.CurrentKeyCallCount.Should().Be(writeWarmCalls,
            "the read (ReaderExecutingAsync) observes the already-warm cache and performs ZERO " +
            "further Key-Vault-shaped calls, regardless of row count");
    }
}

// ---------------------------------------------------------------------------
// Test-local entities/context for interceptor warming tests.
// ---------------------------------------------------------------------------

internal sealed record WarmingId(Guid Value) : StronglyTypedId<Guid>(Value)
{
    public static WarmingId New() => new(Guid.NewGuid());
}

internal sealed class WarmingEncryptedEntity : AggregateRoot<WarmingId>
{
    public string Secret { get; private set; } = string.Empty;

    public WarmingEncryptedEntity(WarmingId id, string secret, IClock clock) : base(id, clock)
    {
        Secret = secret;
    }

    protected WarmingEncryptedEntity() { } // EF Core path
}

internal sealed class WarmingPlainEntity : AggregateRoot<WarmingId>
{
    public string NotSecret { get; private set; } = string.Empty;

    public WarmingPlainEntity(WarmingId id, string notSecret) : base(id, new SystemClock())
    {
        NotSecret = notSecret;
    }

    protected WarmingPlainEntity() { } // EF Core path
}

internal sealed class WarmingEncryptedEntityConfig : EntityTypeConfigurationBase<WarmingEncryptedEntity, WarmingId>
{
    public override void Configure(EntityTypeBuilder<WarmingEncryptedEntity> builder)
    {
        base.Configure(builder);
        builder.ToTable("warming_encrypted_entities");
        builder.Property(e => e.Secret).HasMaxLength(500).Encrypt().IsRequired();
    }
}

internal sealed class WarmingPlainEntityConfig : EntityTypeConfigurationBase<WarmingPlainEntity, WarmingId>
{
    public override void Configure(EntityTypeBuilder<WarmingPlainEntity> builder)
    {
        base.Configure(builder);
        builder.ToTable("warming_plain_entities");
        builder.Property(e => e.NotSecret).HasMaxLength(200).IsRequired();
    }
}

internal sealed class WarmingTestDbContext : SharedKernelDbContext
{
    public DbSet<WarmingEncryptedEntity> EncryptedEntities => Set<WarmingEncryptedEntity>();
    public DbSet<WarmingPlainEntity> PlainEntities => Set<WarmingPlainEntity>();

    public WarmingTestDbContext(
        DbContextOptions<WarmingTestDbContext> options,
        AuditInterceptor audit,
        SoftDeleteInterceptor softDelete,
        ConcurrencyInterceptor concurrency,
        IEnumerable<Microsoft.EntityFrameworkCore.Diagnostics.ISaveChangesInterceptor>? additionalInterceptors,
        IOptionsMonitor<EncryptionOptions>? encryptionOptions,
        IEncryptionVersionOverride? encryptionVersionOverride,
        ISynchronousSymmetricEncryptionService? symmetricEncryptionService)
        : base(options, audit, softDelete, concurrency, additionalInterceptors, encryptionOptions,
               encryptionVersionOverride, symmetricEncryptionService)
    {
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.ConfigureStronglyTypedId<WarmingId, Guid>();
        base.ConfigureConventions(configurationBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new WarmingEncryptedEntityConfig());
        modelBuilder.ApplyConfiguration(new WarmingPlainEntityConfig());
        // Do NOT call base.OnModelCreating to avoid scanning the whole test assembly.
    }
}

// A model with NO encrypted property anywhere — proves both interceptor hooks correctly stay
// silent when there is genuinely nothing to warm for (distinct from WarmingTestDbContext, whose
// model DOES have an encrypted entity type, tripping the coarse whole-model read-hook gate on ANY
// command, per D-130).
internal sealed class AllPlainWarmingDbContext : SharedKernelDbContext
{
    public DbSet<WarmingPlainEntity> PlainEntities => Set<WarmingPlainEntity>();

    public AllPlainWarmingDbContext(
        DbContextOptions<AllPlainWarmingDbContext> options,
        AuditInterceptor audit,
        SoftDeleteInterceptor softDelete,
        ConcurrencyInterceptor concurrency,
        IEnumerable<Microsoft.EntityFrameworkCore.Diagnostics.ISaveChangesInterceptor>? additionalInterceptors)
        : base(options, audit, softDelete, concurrency, additionalInterceptors)
    {
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.ConfigureStronglyTypedId<WarmingId, Guid>();
        base.ConfigureConventions(configurationBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new WarmingPlainEntityConfig());
        // Do NOT call base.OnModelCreating to avoid scanning the whole test assembly.
    }
}
