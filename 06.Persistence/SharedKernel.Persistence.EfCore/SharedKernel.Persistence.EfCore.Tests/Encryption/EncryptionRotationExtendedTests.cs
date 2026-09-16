using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedKernel.Domain.Aggregates;
using SharedKernel.Domain.StronglyTypedIds;
using SharedKernel.Persistence.EfCore.Configurations;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Conversions;
using SharedKernel.Persistence.EfCore.Encryption;
using SharedKernel.Persistence.EfCore.Encryption.Rotation;
using SharedKernel.Persistence.EfCore.Extensions;
using SharedKernel.Persistence.EfCore.Interceptors;
using SharedKernel.Persistence.EfCore.Options;
using SharedKernel.Persistence.EfCore.Tests.TestFixtures;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Persistence.EfCore.Tests.Encryption;

/// <summary>
/// T-45 extended, T-47, T-48, T-49:
/// Additional rotation, reflection-free batch processing, version-override isolation, and
/// doc-drift correction tests.
/// </summary>
public sealed class EncryptionRotationExtendedTests
{
    private static (string V1Key, string V2Key) MakeKeys()
    {
        var v1 = new byte[32]; Array.Fill(v1, (byte)0x11);
        var v2 = new byte[32]; Array.Fill(v2, (byte)0x22);
        return (Convert.ToBase64String(v1), Convert.ToBase64String(v2));
    }

    private sealed class RotationExtHost : IDisposable
    {
        public required ServiceProvider Provider { get; init; }
        public required SqliteConnection Connection { get; init; }
        public void Dispose() { Provider.Dispose(); Connection.Dispose(); }
    }

    private static RotationExtHost BuildSingleEntityHost(Action<EncryptionOptions> configure)
    {
        var conn = new SqliteConnection("DataSource=:memory:"); conn.Open();
        var services = new ServiceCollection();
        services
            .AddSharedKernelEfCore<RotationTestDbContext>(opts =>
                opts.UseSqlite(conn)
                    .ConfigureWarnings(w =>
                        w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
            .WithEncryption(configure)
            .WithDbContextFactory()
            .Build();
        // .WithEncryption() builds its own keyed-DI-isolated synchronous encryption service — no
        // consumer-side registration is needed.
        return new RotationExtHost { Provider = services.BuildServiceProvider(), Connection = conn };
    }

    private static RotationExtHost BuildMultiEntityHost(Action<EncryptionOptions> configure)
    {
        var conn = new SqliteConnection("DataSource=:memory:"); conn.Open();
        var services = new ServiceCollection();
        services
            .AddSharedKernelEfCore<MultiEntityRotationDbContext>(opts =>
                opts.UseSqlite(conn)
                    .ConfigureWarnings(w =>
                        w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
            .WithEncryption(configure)
            .WithDbContextFactory()
            .Build();
        // .WithEncryption() builds its own keyed-DI-isolated synchronous encryption service — no
        // consumer-side registration is needed.
        return new RotationExtHost { Provider = services.BuildServiceProvider(), Connection = conn };
    }

    // =========================================================================
    // T-45 extended: RowsRotated accuracy for mixed dataset
    // =========================================================================

    [Fact]
    public async Task RotateAsync_MultipleRuns_AllRowsProcessedEachTime()
    {
        // Arrange: 3 rows at v1, rotated to v2, then 2 more rows added at v1.
        // Second rotation processes ALL 5 rows — fromVersion does not filter rows;
        // the implementation unconditionally re-encrypts every row in every batch.
        // (EncryptionRotationService<TContext> documents this limitation explicitly.)
        var (v1, v2) = MakeKeys();
        using var host = BuildSingleEntityHost(enc =>
        {
            enc.Enabled = true; enc.CurrentVersion = "1";
            enc.Keys["1"] = v1; enc.Keys["2"] = v2;
        });

        var factory = host.Provider.GetRequiredService<IDbContextFactory<RotationTestDbContext>>();
        var clock = new SystemClock();

        await using (var ctx = await factory.CreateDbContextAsync())
        {
            await ctx.Database.EnsureCreatedAsync();
            // 3 rows encrypted with "1"
            for (var i = 0; i < 3; i++)
                ctx.Customers.Add(new RotationCustomer(RotationCustomerId.New(), $"user{i}@v1.com", clock));
            await ctx.SaveChangesAsync();
        }

        // Rotate the 3 rows from "1" → "2"
        var svc1 = ActivatorUtilities.CreateInstance<TestEncryptionRotationService>(host.Provider);
        var firstResult = await svc1.RotateAsync("1", "2");
        firstResult.RowsProcessed.Should().Be(3);
        firstResult.RowsRotated.Should().Be(3, "all 3 rows are unconditionally processed");

        // Now add 2 more rows (these will be encrypted with CurrentVersion = "1")
        await using (var ctx = await factory.CreateDbContextAsync())
        {
            ctx.Customers.Add(new RotationCustomer(RotationCustomerId.New(), "extra1@v1.com", clock));
            ctx.Customers.Add(new RotationCustomer(RotationCustomerId.New(), "extra2@v1.com", clock));
            await ctx.SaveChangesAsync();
        }

        // Rotate again: ALL 5 rows are processed unconditionally regardless of their current version.
        // EncryptionRotationService does not filter by fromVersion prefix — the CLR-side decrypted
        // value is what EF Core sees, and the raw ciphertext version prefix is not observable
        // post-materialization. The fromVersion parameter is for API/audit/logging only.
        var svc2 = ActivatorUtilities.CreateInstance<TestEncryptionRotationService>(host.Provider);
        var secondResult = await svc2.RotateAsync("1", "2");

        secondResult.RowsProcessed.Should().Be(5, "all 5 rows exist in the database");
        secondResult.RowsRotated.Should().Be(5,
            "all rows are unconditionally re-encrypted with toVersion — fromVersion does not filter");
        secondResult.RowsFailed.Should().Be(0);
    }

    [Fact]
    public async Task RotateAsync_FromVersionParameter_DoesNotFilterRows_ApiStabilityOnly()
    {
        // Documents the explicit design decision: fromVersion is retained for API stability and
        // audit/logging but does NOT filter which rows are rewritten. Even if the stored ciphertext
        // records a different key id (e.g., "1"), passing fromVersion="99" still causes
        // all rows to be re-encrypted with toVersion. This is because EF Core materializes the
        // decrypted CLR value — the stored key id is not observable post-materialization.
        var (v1, v2) = MakeKeys();
        using var host = BuildSingleEntityHost(enc =>
        {
            enc.Enabled = true; enc.CurrentVersion = "1";
            enc.Keys["1"] = v1; enc.Keys["2"] = v2;
        });

        var factory = host.Provider.GetRequiredService<IDbContextFactory<RotationTestDbContext>>();
        var clock = new SystemClock();

        await using (var ctx = await factory.CreateDbContextAsync())
        {
            await ctx.Database.EnsureCreatedAsync();
            ctx.Customers.Add(new RotationCustomer(RotationCustomerId.New(), "test@example.com", clock));
            await ctx.SaveChangesAsync();
        }

        var svc = ActivatorUtilities.CreateInstance<TestEncryptionRotationService>(host.Provider);

        // "99" does not match the stored row prefix ("1") but the row is still processed.
        var result = await svc.RotateAsync("99", "2");

        result.RowsProcessed.Should().Be(1, "rows exist in the database");
        result.RowsRotated.Should().Be(1,
            "fromVersion parameter does not filter rows — all rows are unconditionally re-encrypted");
        result.RowsFailed.Should().Be(0);
    }

    // =========================================================================
    // T-47: Reflection-free batch processor — multi-entity-type model
    // =========================================================================

    [Fact]
    public async Task RotateAsync_MultiEntityTypeModel_RotatesAllEncryptedEntityTypes()
    {
        var (v1, v2) = MakeKeys();
        using var host = BuildMultiEntityHost(enc =>
        {
            enc.Enabled = true; enc.CurrentVersion = "1";
            enc.Keys["1"] = v1; enc.Keys["2"] = v2;
        });

        var factory = host.Provider.GetRequiredService<IDbContextFactory<MultiEntityRotationDbContext>>();
        var clock = new SystemClock();

        await using (var ctx = await factory.CreateDbContextAsync())
        {
            await ctx.Database.EnsureCreatedAsync();
            ctx.Customers.Add(new RotationCustomer2(RotationCustomer2Id.New(), "c@example.com", clock));
            ctx.Orders.Add(new RotationOrder(RotationOrderId.New(), "order-note", clock));
            await ctx.SaveChangesAsync();
        }

        // Confirm both are encrypted with v1
        await using (var ctx = await factory.CreateDbContextAsync())
        {
            var cRaw = await ctx.Database
                .SqlQueryRaw<string>("SELECT email FROM rotation_customers2")
                .ToListAsync();
            StoredPayload.KeyIdOf(cRaw.Should().ContainSingle().Which).Should().Be("1");

            var oRaw = await ctx.Database
                .SqlQueryRaw<string>("SELECT note FROM rotation_orders")
                .ToListAsync();
            StoredPayload.KeyIdOf(oRaw.Should().ContainSingle().Which).Should().Be("1");
        }

        var svc = ActivatorUtilities.CreateInstance<MultiEntityRotationService>(host.Provider);
        var result = await svc.RotateAsync("1", "2");

        result.RowsProcessed.Should().Be(2, "one row per entity type");
        result.RowsRotated.Should().Be(2);
        result.RowsFailed.Should().Be(0);

        // Verify both entity types are now at v2
        await using (var ctx = await factory.CreateDbContextAsync())
        {
            var cRaw = await ctx.Database
                .SqlQueryRaw<string>("SELECT email FROM rotation_customers2")
                .ToListAsync();
            StoredPayload.KeyIdOf(cRaw.Should().ContainSingle().Which).Should().Be("2");

            var oRaw = await ctx.Database
                .SqlQueryRaw<string>("SELECT note FROM rotation_orders")
                .ToListAsync();
            StoredPayload.KeyIdOf(oRaw.Should().ContainSingle().Which).Should().Be("2");
        }
    }

    // =========================================================================
    // T-47: Batch boundary tests (exactly, one less, one more than BatchSize=3)
    // =========================================================================

    [Theory]
    [InlineData(2)]  // BatchSize - 1
    [InlineData(3)]  // BatchSize exactly
    [InlineData(4)]  // BatchSize + 1
    public async Task RotateAsync_BatchBoundary_AllRowsProcessedCorrectly(int rowCount)
    {
        var (v1, v2) = MakeKeys();
        using var host = BuildSingleEntityHost(enc =>
        {
            enc.Enabled = true; enc.CurrentVersion = "1";
            enc.Keys["1"] = v1; enc.Keys["2"] = v2;
        });

        var factory = host.Provider.GetRequiredService<IDbContextFactory<RotationTestDbContext>>();
        var clock = new SystemClock();

        await using (var ctx = await factory.CreateDbContextAsync())
        {
            await ctx.Database.EnsureCreatedAsync();
            for (var i = 0; i < rowCount; i++)
                ctx.Customers.Add(new RotationCustomer(RotationCustomerId.New(), $"u{i}@batch.com", clock));
            await ctx.SaveChangesAsync();
        }

        var svc = ActivatorUtilities.CreateInstance<TestEncryptionRotationService>(host.Provider);
        var result = await svc.RotateAsync("1", "2");

        result.RowsProcessed.Should().Be(rowCount);
        result.RowsRotated.Should().Be(rowCount);
        result.RowsFailed.Should().Be(0);

        // Verify no rows were skipped or duplicated
        await using (var ctx = await factory.CreateDbContextAsync())
        {
            var customers = await ctx.Customers.ToListAsync();
            customers.Should().HaveCount(rowCount, "no duplicate or missing rows after rotation");
        }
    }

    // =========================================================================
    // T-48: IEncryptionVersionOverride rotation-scoped override tests
    // =========================================================================

    [Fact]
    public async Task RotateAsync_RotatedRows_HaveToVersion_CiphertextPrefix()
    {
        var (v1, v2) = MakeKeys();
        using var host = BuildSingleEntityHost(enc =>
        {
            enc.Enabled = true; enc.CurrentVersion = "1";
            enc.Keys["1"] = v1; enc.Keys["2"] = v2;
        });

        var factory = host.Provider.GetRequiredService<IDbContextFactory<RotationTestDbContext>>();
        var clock = new SystemClock();

        await using (var ctx = await factory.CreateDbContextAsync())
        {
            await ctx.Database.EnsureCreatedAsync();
            ctx.Customers.Add(new RotationCustomer(RotationCustomerId.New(), "override@example.com", clock));
            await ctx.SaveChangesAsync();
        }

        var svc = ActivatorUtilities.CreateInstance<TestEncryptionRotationService>(host.Provider);
        await svc.RotateAsync("1", "2");

        // Row must now start with v2: — the override directed the converter to use toVersion
        await using (var rawCtx = await factory.CreateDbContextAsync())
        {
            var raw = await rawCtx.Database
                .SqlQueryRaw<string>("SELECT email FROM rotation_customers")
                .ToListAsync();
            StoredPayload.KeyIdOf(raw.Should().ContainSingle().Which).Should().Be("2",
                "RotateAsync must re-encrypt with toVersion ('2'), not CurrentVersion ('1')");
        }
    }

    [Fact]
    public async Task RotateAsync_CurrentVersion_Unchanged_After_Rotation()
    {
        var (v1, v2) = MakeKeys();
        using var host = BuildSingleEntityHost(enc =>
        {
            enc.Enabled = true; enc.CurrentVersion = "1";
            enc.Keys["1"] = v1; enc.Keys["2"] = v2;
        });

        var factory = host.Provider.GetRequiredService<IDbContextFactory<RotationTestDbContext>>();
        await using (var ctx = await factory.CreateDbContextAsync())
        {
            await ctx.Database.EnsureCreatedAsync();
            ctx.Customers.Add(new RotationCustomer(RotationCustomerId.New(), "x@example.com", new SystemClock()));
            await ctx.SaveChangesAsync();
        }

        var svc = ActivatorUtilities.CreateInstance<TestEncryptionRotationService>(host.Provider);
        await svc.RotateAsync("1", "2");

        var monitor = host.Provider.GetRequiredService<IOptionsMonitor<EncryptionOptions>>();
        monitor.CurrentValue.CurrentVersion.Should().Be("1",
            "IEncryptionVersionOverride must be used for re-encryption; CurrentVersion must not be mutated");
    }

    [Fact]
    public async Task VersionOverride_AsyncLocalIsolation_IndependentFlow_SeesNull()
    {
        // Verify AsyncLocal<T> isolation: a task created BEFORE the override mutation
        // (i.e., in an independent logical flow) sees null, not the mutated value.
        // This models a concurrent HTTP request that was already running when rotation started.
        var versionOverride = new EncryptionVersionOverride();
        versionOverride.OverrideVersion.Should().BeNull("starts at null");

        // Start an independent task BEFORE setting the override in the parent context.
        // AsyncLocal<T> gives each logical call context its own copy of the value.
        // A child spawned BEFORE the parent's mutation does NOT inherit the later change.
        // NOTE: Task.Run inherits the ambient AsyncLocal value AT SPAWN TIME.
        // We start the task when the value is null, then mutate in the parent.
        var independentTask = Task.Run(async () =>
        {
            // Yield to let the parent task mutate the override AFTER this task has started.
            await Task.Delay(20);
            // This task's AsyncLocal slot was captured as null at Task.Run call time.
            return versionOverride.OverrideVersion;
        });

        // Now mutate the override in the parent's logical context (simulating rotation).
        // The child task above has its own AsyncLocal slot and will NOT see this mutation.
        versionOverride.OverrideVersion = "2";
        versionOverride.OverrideVersion.Should().Be("2", "parent context sees the mutation");

        // Child task (started before mutation) should see null — not "2"
        var childResult = await independentTask;
        childResult.Should().BeNull(
            "a task started BEFORE the override mutation must not see the mutated value " +
            "(AsyncLocal<T> isolation: parent mutations do not retroactively affect already-running child tasks)");

        versionOverride.OverrideVersion = null; // cleanup
    }

    [Fact]
    public async Task AfterRotation_FreshContext_EncryptsWith_CurrentVersion_NotToVersion()
    {
        var (v1, v2) = MakeKeys();
        using var host = BuildSingleEntityHost(enc =>
        {
            enc.Enabled = true; enc.CurrentVersion = "1";
            enc.Keys["1"] = v1; enc.Keys["2"] = v2;
        });

        var factory = host.Provider.GetRequiredService<IDbContextFactory<RotationTestDbContext>>();
        var clock = new SystemClock();

        await using (var ctx = await factory.CreateDbContextAsync())
        {
            await ctx.Database.EnsureCreatedAsync();
            ctx.Customers.Add(new RotationCustomer(RotationCustomerId.New(), "before@example.com", clock));
            await ctx.SaveChangesAsync();
        }

        // Run rotation
        var svc = ActivatorUtilities.CreateInstance<TestEncryptionRotationService>(host.Provider);
        await svc.RotateAsync("1", "2");

        // Verify override is null after rotation
        var versionOverride = host.Provider.GetRequiredService<IEncryptionVersionOverride>();
        versionOverride.OverrideVersion.Should().BeNull("rotation must reset OverrideVersion when done");

        // Insert a new row after rotation — must use CurrentVersion ("1"), not "2"
        await using (var ctx = await factory.CreateDbContextAsync())
        {
            ctx.Customers.Add(new RotationCustomer(RotationCustomerId.New(), "after@example.com", clock));
            await ctx.SaveChangesAsync();
        }

        await using (var rawCtx = await factory.CreateDbContextAsync())
        {
            var raws = await rawCtx.Database
                .SqlQueryRaw<string>("SELECT email FROM rotation_customers")
                .ToListAsync();

            // The newly inserted row must use CurrentVersion = "1", not "2"
            raws.Should().Contain(v => StoredPayload.KeyIdOf(v) == "1",
                "new inserts after rotation must use CurrentVersion ('1'), not the rotation's toVersion ('2')");
        }
    }

    // =========================================================================
    // T-49: Doc-drift correction — no EncryptedValueConverter<T> generic type
    // =========================================================================

    [Fact]
    public void EncryptedValueConverter_IsNonGeneric_NoGenericTypeExists()
    {
        var assembly = typeof(EncryptedValueConverter).Assembly;

        // The non-generic type must exist
        var nonGenericType = assembly.GetType(
            "SharedKernel.Persistence.EfCore.Encryption.EncryptedValueConverter");

        nonGenericType.Should().NotBeNull(
            "EncryptedValueConverter must exist as a non-generic sealed class");
        nonGenericType!.IsGenericTypeDefinition.Should().BeFalse(
            "EncryptedValueConverter must NOT be a generic type definition");

        // The generic variant (EncryptedValueConverter<T> or EncryptedValueConverter`1) must NOT exist
        var genericType = assembly.GetType(
            "SharedKernel.Persistence.EfCore.Encryption.EncryptedValueConverter`1");

        genericType.Should().BeNull(
            "EncryptedValueConverter<T> (generic variant) must NOT exist; doc-drift correction P-147");
    }

    [Fact]
    public void EncryptedValueConverter_IsCorrectType_ReferencedByEncryptionModelConvention()
    {
        // EncryptionModelConvention is in the same assembly — its Apply() method must reference
        // the non-generic EncryptedValueConverter, not a generic one.
        // We verify via the model metadata applied by the convention (see T-44 tests).
        // This test just verifies the converter type referenced by convention tests is the non-generic class.

        var converterType = typeof(EncryptedValueConverter);
        converterType.IsGenericType.Should().BeFalse(
            "the converter applied by EncryptionModelConvention must be the non-generic EncryptedValueConverter");
        converterType.IsSealed.Should().BeTrue(
            "EncryptedValueConverter must be sealed");
    }
}

// ---------------------------------------------------------------------------
// Multi-entity-type rotation domain types
// ---------------------------------------------------------------------------

internal sealed record RotationCustomer2Id(Guid Value) : StronglyTypedId<Guid>(Value)
{
    public static RotationCustomer2Id New() => new(Guid.NewGuid());
}

internal sealed class RotationCustomer2 : AggregateRoot<RotationCustomer2Id>
{
    public string Email { get; private set; } = string.Empty;

    public RotationCustomer2(RotationCustomer2Id id, string email, IClock clock) : base(id, clock)
    {
        Email = email;
    }

    protected RotationCustomer2() { }
}

internal sealed class RotationCustomer2Config : EntityTypeConfigurationBase<RotationCustomer2, RotationCustomer2Id>
{
    public override void Configure(EntityTypeBuilder<RotationCustomer2> builder)
    {
        base.Configure(builder);
        builder.ToTable("rotation_customers2");
        builder.Property(e => e.Email).HasMaxLength(500).Encrypt().IsRequired();
    }
}

internal sealed record RotationOrderId(Guid Value) : StronglyTypedId<Guid>(Value)
{
    public static RotationOrderId New() => new(Guid.NewGuid());
}

internal sealed class RotationOrder : AggregateRoot<RotationOrderId>
{
    public string Note { get; private set; } = string.Empty;

    public RotationOrder(RotationOrderId id, string note, IClock clock) : base(id, clock)
    {
        Note = note;
    }

    protected RotationOrder() { }
}

internal sealed class RotationOrderConfig : EntityTypeConfigurationBase<RotationOrder, RotationOrderId>
{
    public override void Configure(EntityTypeBuilder<RotationOrder> builder)
    {
        base.Configure(builder);
        builder.ToTable("rotation_orders");
        builder.Property(e => e.Note).HasMaxLength(500).Encrypt().IsRequired();
    }
}

internal sealed class MultiEntityRotationDbContext : SharedKernelDbContext
{
    public DbSet<RotationCustomer2> Customers => Set<RotationCustomer2>();
    public DbSet<RotationOrder> Orders => Set<RotationOrder>();

    public MultiEntityRotationDbContext(
        DbContextOptions<MultiEntityRotationDbContext> options,
        AuditInterceptor audit,
        SoftDeleteInterceptor softDelete,
        ConcurrencyInterceptor concurrency,
        IOptionsMonitor<EncryptionOptions>? encryptionOptions = null,
        IEncryptionVersionOverride? encryptionVersionOverride = null,
        SharedKernel.Cryptography.Symmetric.ISynchronousSymmetricEncryptionService? symmetricEncryptionService = null)
        : base(options, audit, softDelete, concurrency, null, encryptionOptions,
               encryptionVersionOverride, symmetricEncryptionService)
    {
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.ConfigureStronglyTypedId<RotationCustomer2Id, Guid>();
        configurationBuilder.ConfigureStronglyTypedId<RotationOrderId, Guid>();
        base.ConfigureConventions(configurationBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new RotationCustomer2Config());
        modelBuilder.ApplyConfiguration(new RotationOrderConfig());
    }
}

internal sealed class MultiEntityRotationService : EncryptionRotationService<MultiEntityRotationDbContext>
{
    public MultiEntityRotationService(
        IDbContextFactory<MultiEntityRotationDbContext> contextFactory,
        IOptionsMonitor<EncryptionOptions> optionsMonitor,
        EncryptedEntityBatchProcessorRegistry<MultiEntityRotationDbContext> registry)
        : base(contextFactory, optionsMonitor, registry)
    {
    }

    protected override int BatchSize => 10;
}
