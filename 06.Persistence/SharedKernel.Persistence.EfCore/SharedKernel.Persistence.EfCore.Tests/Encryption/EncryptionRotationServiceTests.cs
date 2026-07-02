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
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Persistence.EfCore.Tests.Encryption;

/// <summary>
/// Integration tests for <see cref="EncryptedEntityBatchProcessor{TEntity}"/>,
/// <see cref="EncryptedEntityBatchProcessorRegistry{TContext}"/>, <see cref="IEncryptionVersionOverride"/>,
/// and <see cref="EncryptionRotationService{TContext}"/> (C-82, C-83).
/// </summary>
public sealed class EncryptionRotationServiceTests
{
    private static (string V1Key, string V2Key) MakeKeys()
    {
        var v1 = new byte[32];
        var v2 = new byte[32];
        Array.Fill(v1, (byte)0x11);
        Array.Fill(v2, (byte)0x22);
        return (Convert.ToBase64String(v1), Convert.ToBase64String(v2));
    }

    // Holds an externally-opened SqliteConnection alongside the built ServiceProvider so the
    // in-memory database survives across multiple IDbContextFactory-created contexts within a
    // single test. Disposing this disposes both the provider and the connection.
    private sealed class RotationTestHost : IDisposable
    {
        public required ServiceProvider Provider { get; init; }
        public required SqliteConnection Connection { get; init; }

        public void Dispose()
        {
            Provider.Dispose();
            Connection.Dispose();
        }
    }

    private static RotationTestHost BuildProvider(Action<EncryptionOptions> configure)
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var services = new ServiceCollection();

        services
            .AddSharedKernelEfCore<RotationTestDbContext>(opts =>
                opts.UseSqlite(connection)
                    .ConfigureWarnings(w =>
                        w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
            .WithEncryption(configure)
            .WithDbContextFactory()
            .Build();

        // P-227: ISymmetricEncryptionService must be registered (consuming service responsibility).
        // Tests register AesGcmEncryptionService as scoped (scoped to match the scoped IEncryptionKeyProvider
        // registered by WithEncryption() as EncryptionOptionsKeyProvider).
        services.AddScoped<SharedKernel.Cryptography.Symmetric.ISymmetricEncryptionService,
            SharedKernel.Cryptography.Symmetric.AesGcmEncryptionService>();

        var provider = services.BuildServiceProvider();
        return new RotationTestHost { Provider = provider, Connection = connection };
    }

    // ---------------------------------------------------------------------------
    // EncryptedEntityBatchProcessor<TEntity> / EncryptedEntityBatchProcessorRegistry (C-82)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task BatchProcessorRegistry_TryGet_Returns_Processor_For_Encrypted_Entity()
    {
        var (v1, _) = MakeKeys();

        using var host = BuildProvider(enc =>
        {
            enc.Enabled = true;
            enc.CurrentVersion = "1";
            enc.Keys["1"] = v1;
        });

        var factory = host.Provider.GetRequiredService<IDbContextFactory<RotationTestDbContext>>();
        await using (var ctx = await factory.CreateDbContextAsync())
        {
            await ctx.Database.EnsureCreatedAsync();
        }

        var registry = host.Provider.GetRequiredService<EncryptedEntityBatchProcessorRegistry<RotationTestDbContext>>();

        var found = registry.TryGet(typeof(RotationCustomer), out var processor);

        found.Should().BeTrue("RotationCustomer has an .Encrypt()-annotated property and must be registered");
        processor.Should().NotBeNull();
        processor.Should().BeOfType<EncryptedEntityBatchProcessor<RotationCustomer>>();
    }

    [Fact]
    public async Task BatchProcessorRegistry_TryGet_Returns_False_For_NonEncrypted_Entity()
    {
        var (v1, _) = MakeKeys();

        using var host = BuildProvider(enc =>
        {
            enc.Enabled = true;
            enc.CurrentVersion = "1";
            enc.Keys["1"] = v1;
        });

        var factory = host.Provider.GetRequiredService<IDbContextFactory<RotationTestDbContext>>();
        await using (var ctx = await factory.CreateDbContextAsync())
        {
            await ctx.Database.EnsureCreatedAsync();
        }

        var registry = host.Provider.GetRequiredService<EncryptedEntityBatchProcessorRegistry<RotationTestDbContext>>();

        var found = registry.TryGet(typeof(string), out var processor);

        found.Should().BeFalse();
        processor.Should().BeNull();
    }

    [Fact]
    public async Task BatchProcessor_LoadBatchAsync_Loads_Rows_Without_Reflection_Cast()
    {
        var (v1, _) = MakeKeys();

        using var host = BuildProvider(enc =>
        {
            enc.Enabled = true;
            enc.CurrentVersion = "1";
            enc.Keys["1"] = v1;
        });

        var factory = host.Provider.GetRequiredService<IDbContextFactory<RotationTestDbContext>>();
        var clock = new SystemClock();

        await using (var ctx = await factory.CreateDbContextAsync())
        {
            await ctx.Database.EnsureCreatedAsync();
            ctx.Customers.Add(new RotationCustomer(RotationCustomerId.New(), "a@example.com", clock));
            ctx.Customers.Add(new RotationCustomer(RotationCustomerId.New(), "b@example.com", clock));
            await ctx.SaveChangesAsync();
        }

        var processor = new EncryptedEntityBatchProcessor<RotationCustomer>();

        await using var readCtx = await factory.CreateDbContextAsync();
        var batch = await processor.LoadBatchAsync(readCtx, skip: 0, take: 10, CancellationToken.None);

        batch.Should().HaveCount(2);
        batch.Should().AllBeOfType<RotationCustomer>();
    }

    // ---------------------------------------------------------------------------
    // IEncryptionVersionOverride singleton, AsyncLocal-backed seam (C-83)
    // ---------------------------------------------------------------------------

    [Fact]
    public void EncryptionVersionOverride_Defaults_To_Null_OverrideVersion()
    {
        var instance = new EncryptionVersionOverride();
        instance.OverrideVersion.Should().BeNull();
    }

    [Fact]
    public async Task IEncryptionVersionOverride_Is_Registered_As_Singleton_When_WithEncryption_Called()
    {
        var (v1, _) = MakeKeys();

        using var host = BuildProvider(enc =>
        {
            enc.Enabled = true;
            enc.CurrentVersion = "1";
            enc.Keys["1"] = v1;
        });

        using var scope1 = host.Provider.CreateScope();
        using var scope2 = host.Provider.CreateScope();

        var override1 = scope1.ServiceProvider.GetRequiredService<IEncryptionVersionOverride>();
        var override2 = scope2.ServiceProvider.GetRequiredService<IEncryptionVersionOverride>();

        override1.Should().BeSameAs(override2,
            "IEncryptionVersionOverride must be a singleton — EF Core caches the compiled model " +
            "(and its EncryptedValueConverter instances) across DbContext instances, so a scoped " +
            "registration would only be observed by the first context's converters");

        await Task.CompletedTask;
    }

    [Fact]
    public async Task IEncryptionVersionOverride_OverrideVersion_Does_Not_Leak_Across_Unrelated_AsyncLocal_Flows()
    {
        var instance = new EncryptionVersionOverride();

        instance.OverrideVersion.Should().BeNull();

        var task1 = Task.Run(async () =>
        {
            instance.OverrideVersion = "2";
            await Task.Delay(10);
            return instance.OverrideVersion;
        });

        var task2 = Task.Run(async () =>
        {
            await Task.Delay(5);
            return instance.OverrideVersion;
        });

        var results = await Task.WhenAll(task1, task2);

        // The flow that set "2" observes it; the unrelated flow that never set it observes null —
        // AsyncLocal<T> isolates the value per logical call context.
        results[0].Should().Be("2");
        results[1].Should().BeNull();
    }

    // ---------------------------------------------------------------------------
    // EncryptionRotationService<TContext>.RotateAsync end-to-end (C-82, C-83)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task RotateAsync_ReEncrypts_Rows_From_OldVersion_To_NewVersion()
    {
        var (v1, v2) = MakeKeys();

        using var host = BuildProvider(enc =>
        {
            enc.Enabled = true;
            enc.CurrentVersion = "1";
            enc.Keys["1"] = v1;
            enc.Keys["2"] = v2;
        });

        var factory = host.Provider.GetRequiredService<IDbContextFactory<RotationTestDbContext>>();
        var clock = new SystemClock();
        RotationCustomerId id1, id2;

        await using (var ctx = await factory.CreateDbContextAsync())
        {
            await ctx.Database.EnsureCreatedAsync();
            id1 = RotationCustomerId.New();
            id2 = RotationCustomerId.New();
            ctx.Customers.Add(new RotationCustomer(id1, "alice@example.com", clock));
            ctx.Customers.Add(new RotationCustomer(id2, "bob@example.com", clock));
            await ctx.SaveChangesAsync();
        }

        // Confirm rows are stored with the "v1:" prefix before rotation.
        await using (var rawCtx = await factory.CreateDbContextAsync())
        {
            var raw = await rawCtx.Database
                .SqlQueryRaw<string>("SELECT email FROM rotation_customers ORDER BY email")
                .ToListAsync();
            raw.Should().HaveCount(2);
            raw.Should().AllSatisfy(v => v.Should().StartWith("v1:"));
        }

        var rotationService = ActivatorUtilities.CreateInstance<TestEncryptionRotationService>(host.Provider);

        var result = await rotationService.RotateAsync("1", "2");

        result.RowsProcessed.Should().Be(2);
        result.RowsRotated.Should().Be(2);
        result.RowsFailed.Should().Be(0);
        result.Errors.Should().BeEmpty();

        // Confirm rows are now stored with the "v2:" prefix.
        await using (var rawCtx = await factory.CreateDbContextAsync())
        {
            var raw = await rawCtx.Database
                .SqlQueryRaw<string>("SELECT email FROM rotation_customers ORDER BY email")
                .ToListAsync();
            raw.Should().AllSatisfy(v => v.Should().StartWith("v2:"));
        }

        // Confirm values decrypt to the original plaintext after rotation.
        await using (var ctx = await factory.CreateDbContextAsync())
        {
            var customers = await ctx.Customers.ToListAsync();
            customers.Should().Contain(c => c.Email == "alice@example.com");
            customers.Should().Contain(c => c.Email == "bob@example.com");
        }
    }

    [Fact]
    public async Task RotateAsync_Does_Not_Mutate_CurrentVersion_Option()
    {
        var (v1, v2) = MakeKeys();

        using var host = BuildProvider(enc =>
        {
            enc.Enabled = true;
            enc.CurrentVersion = "1";
            enc.Keys["1"] = v1;
            enc.Keys["2"] = v2;
        });

        var factory = host.Provider.GetRequiredService<IDbContextFactory<RotationTestDbContext>>();
        var clock = new SystemClock();

        await using (var ctx = await factory.CreateDbContextAsync())
        {
            await ctx.Database.EnsureCreatedAsync();
            ctx.Customers.Add(new RotationCustomer(RotationCustomerId.New(), "carol@example.com", clock));
            await ctx.SaveChangesAsync();
        }

        var rotationService = ActivatorUtilities.CreateInstance<TestEncryptionRotationService>(host.Provider);
        await rotationService.RotateAsync("1", "2");

        var optionsMonitor = host.Provider.GetRequiredService<IOptionsMonitor<EncryptionOptions>>();
        optionsMonitor.CurrentValue.CurrentVersion.Should().Be("1",
            "RotateAsync must direct converters via IEncryptionVersionOverride, not mutate CurrentVersion");
    }

    [Fact]
    public async Task RotateAsync_Is_Idempotent_When_Run_Twice()
    {
        var (v1, v2) = MakeKeys();

        using var host = BuildProvider(enc =>
        {
            enc.Enabled = true;
            enc.CurrentVersion = "1";
            enc.Keys["1"] = v1;
            enc.Keys["2"] = v2;
        });

        var factory = host.Provider.GetRequiredService<IDbContextFactory<RotationTestDbContext>>();
        var clock = new SystemClock();

        await using (var ctx = await factory.CreateDbContextAsync())
        {
            await ctx.Database.EnsureCreatedAsync();
            ctx.Customers.Add(new RotationCustomer(RotationCustomerId.New(), "dave@example.com", clock));
            await ctx.SaveChangesAsync();
        }

        var rotationService = ActivatorUtilities.CreateInstance<TestEncryptionRotationService>(host.Provider);

        var first = await rotationService.RotateAsync("1", "2");
        first.RowsRotated.Should().Be(1);

        // Second run: rotation is unconditional per batch, so the row is re-encrypted again with
        // "v2" (a no-op data change). "Idempotent" means the result remains valid and decryptable,
        // and EncryptionOptions.CurrentVersion is never mutated — not that RowsRotated becomes zero.
        var second = await rotationService.RotateAsync("1", "2");
        second.RowsRotated.Should().Be(1);
        second.RowsFailed.Should().Be(0);

        await using (var ctx = await factory.CreateDbContextAsync())
        {
            var customer = await ctx.Customers.SingleAsync();
            customer.Email.Should().Be("dave@example.com");
        }
    }

    [Fact]
    public async Task Diagnostic_OverrideVersion_Plus_IsModified_ReEncrypts_With_New_Version()
    {
        var (v1, v2) = MakeKeys();

        using var host = BuildProvider(enc =>
        {
            enc.Enabled = true;
            enc.CurrentVersion = "1";
            enc.Keys["1"] = v1;
            enc.Keys["2"] = v2;
        });

        var factory = host.Provider.GetRequiredService<IDbContextFactory<RotationTestDbContext>>();
        var clock = new SystemClock();
        RotationCustomerId id;

        await using (var ctx = await factory.CreateDbContextAsync())
        {
            await ctx.Database.EnsureCreatedAsync();
            id = RotationCustomerId.New();
            ctx.Customers.Add(new RotationCustomer(id, "diag@example.com", clock));
            await ctx.SaveChangesAsync();
        }

        await using (var rawCtx = await factory.CreateDbContextAsync())
        {
            var raw = await rawCtx.Database
                .SqlQueryRaw<string>("SELECT email FROM rotation_customers")
                .ToListAsync();
            raw.Should().ContainSingle().Which.Should().StartWith("v1:");
        }

        // Directly mark the property modified and override the encryption version, without going
        // through EncryptionRotationService, to isolate the converter+override interaction.
        await using (var ctx = await factory.CreateDbContextAsync())
        {
            var processor = new EncryptedEntityBatchProcessor<RotationCustomer>();
            var batch = await processor.LoadBatchAsync(ctx, skip: 0, take: 10, CancellationToken.None);
            var customer = (RotationCustomer)batch.Single();
            var entry = ctx.Entry(customer);

            var versionOverride = ctx.CurrentEncryptionVersionOverride;

            // Sanity check: this must be the SAME singleton instance the model convention captured.
            versionOverride.Should().BeSameAs(host.Provider.GetRequiredService<IEncryptionVersionOverride>());

            ctx.ChangeTracker.AutoDetectChangesEnabled = false;
            entry.Property(nameof(RotationCustomer.Email)).IsModified = true;

            versionOverride.OverrideVersion = "2";
            try
            {
                await ctx.SaveChangesAsync();
            }
            finally
            {
                versionOverride.OverrideVersion = null;
            }
        }

        await using (var rawCtx = await factory.CreateDbContextAsync())
        {
            var raw = await rawCtx.Database
                .SqlQueryRaw<string>("SELECT email FROM rotation_customers")
                .ToListAsync();
            raw.Should().ContainSingle().Which.Should().StartWith("v2:");
        }
    }

    [Fact]
    public async Task RotateAsync_Handles_BatchSize_Boundary_Correctly()
    {
        var (v1, v2) = MakeKeys();

        using var host = BuildProvider(enc =>
        {
            enc.Enabled = true;
            enc.CurrentVersion = "1";
            enc.Keys["1"] = v1;
            enc.Keys["2"] = v2;
        });

        var factory = host.Provider.GetRequiredService<IDbContextFactory<RotationTestDbContext>>();
        var clock = new SystemClock();

        // TestEncryptionRotationService.BatchSize == 3. Insert exactly 7 rows
        // (two full batches of 3, plus a partial final batch of 1).
        await using (var ctx = await factory.CreateDbContextAsync())
        {
            await ctx.Database.EnsureCreatedAsync();
            for (var i = 0; i < 7; i++)
            {
                ctx.Customers.Add(new RotationCustomer(RotationCustomerId.New(), $"user{i}@example.com", clock));
            }
            await ctx.SaveChangesAsync();
        }

        var rotationService = ActivatorUtilities.CreateInstance<TestEncryptionRotationService>(host.Provider);
        var result = await rotationService.RotateAsync("1", "2");

        result.RowsProcessed.Should().Be(7);
        result.RowsRotated.Should().Be(7);
        result.RowsFailed.Should().Be(0);
    }
}

// ---------------------------------------------------------------------------
// Test-local entity, ID, configuration, DbContext, and rotation service
// ---------------------------------------------------------------------------

internal sealed record RotationCustomerId(Guid Value) : StronglyTypedId<Guid>(Value)
{
    public static RotationCustomerId New() => new(Guid.NewGuid());
}

internal sealed class RotationCustomer : AggregateRoot<RotationCustomerId>
{
    public string Email { get; private set; } = string.Empty;

    public RotationCustomer(RotationCustomerId id, string email, IClock clock)
        : base(id, clock)
    {
        Email = email;
    }

    protected RotationCustomer() { } // EF Core path
}

internal sealed class RotationCustomerConfig : EntityTypeConfigurationBase<RotationCustomer, RotationCustomerId>
{
    public override void Configure(EntityTypeBuilder<RotationCustomer> builder)
    {
        base.Configure(builder);
        builder.ToTable("rotation_customers");
        builder.Property(e => e.Email).HasMaxLength(500).Encrypt().IsRequired();
    }
}

/// <summary>
/// DbContext used by <see cref="EncryptionRotationServiceTests"/>. Unlike
/// <c>EncryptedTestDbContext</c>, this context forwards <see cref="IEncryptionVersionOverride"/>
/// from DI so that <see cref="EncryptionRotationService{TContext}"/> can direct the model's
/// <see cref="EncryptedValueConverter"/> instances to a target key version during a rotation batch.
/// </summary>
internal sealed class RotationTestDbContext : SharedKernelDbContext
{
    public DbSet<RotationCustomer> Customers => Set<RotationCustomer>();

    public RotationTestDbContext(
        DbContextOptions<RotationTestDbContext> options,
        AuditInterceptor audit,
        SoftDeleteInterceptor softDelete,
        ConcurrencyInterceptor concurrency,
        IOptionsMonitor<EncryptionOptions>? encryptionOptions = null,
        IEncryptionVersionOverride? encryptionVersionOverride = null,
        SharedKernel.Cryptography.Symmetric.ISymmetricEncryptionService? symmetricEncryptionService = null,
        SharedKernel.Cryptography.Symmetric.IEncryptionKeyProvider? encryptionKeyProvider = null)
        : base(options, audit, softDelete, concurrency, null, encryptionOptions,
               encryptionVersionOverride, symmetricEncryptionService, encryptionKeyProvider)
    {
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.ConfigureStronglyTypedId<RotationCustomerId, Guid>();
        base.ConfigureConventions(configurationBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new RotationCustomerConfig());
        // Do NOT call base.OnModelCreating to avoid scanning the whole test assembly.
    }
}

/// <summary>
/// Concrete <see cref="EncryptionRotationService{TContext}"/> with a small <see cref="BatchSize"/>
/// (3) to exercise batch-boundary behavior with a modest number of test rows.
/// </summary>
internal sealed class TestEncryptionRotationService : EncryptionRotationService<RotationTestDbContext>
{
    public TestEncryptionRotationService(
        IDbContextFactory<RotationTestDbContext> contextFactory,
        IOptionsMonitor<EncryptionOptions> optionsMonitor,
        EncryptedEntityBatchProcessorRegistry<RotationTestDbContext> registry)
        : base(contextFactory, optionsMonitor, registry)
    {
    }

    protected override int BatchSize => 3;
}
