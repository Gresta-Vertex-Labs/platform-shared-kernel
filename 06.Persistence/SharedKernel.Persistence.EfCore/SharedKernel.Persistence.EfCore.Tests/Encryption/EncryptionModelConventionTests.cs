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

namespace SharedKernel.Persistence.EfCore.Tests.Encryption;

/// <summary>
/// Integration tests for <see cref="EncryptionModelConvention"/> and the
/// <c>.Encrypt()</c> extension method — verifies convention wires the converter correctly and
/// that encrypted properties round-trip through EF Core with SQLite.
/// Tests use the real <see cref="SynchronousAesGcmEncryptionService"/> wired via
/// <see cref="EncryptionOptionsKeyProvider"/>.
/// </summary>
public sealed class EncryptionModelConventionTests
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
    /// Creates a fully-wired <see cref="EncryptedTestDbContext"/> using the real delegation chain:
    /// EncryptionOptionsKeyProvider → SynchronousAesGcmEncryptionService → SharedKernelDbContext(encryptionService).
    /// </summary>
    private static EncryptedTestDbContext MakeContext(
        DbContextOptions<EncryptedTestDbContext> dbOptions,
        AuditInterceptor audit,
        SoftDeleteInterceptor softDelete,
        ConcurrencyInterceptor concurrency,
        EncryptionOptions opts,
        IEncryptionVersionOverride? versionOverride = null)
    {
        var monitor = MakeMonitor(opts);
        var keyProvider = new EncryptionOptionsKeyProvider(monitor, versionOverride ?? EncryptionVersionOverride.NoOp);
        var encryptionService = new SynchronousAesGcmEncryptionService(keyProvider);
        return new EncryptedTestDbContext(dbOptions, audit, softDelete, concurrency, monitor, encryptionService, versionOverride);
    }

    [Fact]
    public async Task EncryptedProperty_RoundTrips_Through_EfCore_SQLite()
    {
        // Arrange — create context with encryption enabled
        var opts = EnabledOptions();
        var userCtx = TestDbContextFactory.CreateAuthenticatedUserContext(Guid.NewGuid());
        var clock = TestDbContextFactory.CreateClock(DateTimeOffset.UtcNow);
        var svcOpts = TestDbContextFactory.DefaultServiceOptions();

        var audit = new AuditInterceptor(userCtx, clock, svcOpts);
        var softDelete = new SoftDeleteInterceptor(userCtx, clock, svcOpts);
        var concurrency = new ConcurrencyInterceptor();

        var dbOptions = new DbContextOptionsBuilder<EncryptedTestDbContext>()
            .UseSqlite($"DataSource=file:{Guid.NewGuid():N}?mode=memory&cache=shared")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;

        var id = EncryptedTestId.New();
        const string email = "test-user@example.com";

        await using (var ctx = MakeContext(dbOptions, audit, softDelete, concurrency, opts))
        {
            ctx.Database.EnsureCreated();

            // Act — persist entity with encrypted property
            ctx.Customers.Add(new EncryptedTestCustomer(id, email, clock));
            await ctx.SaveChangesAsync();
        }

        // Re-open context — verify the value decrypts correctly on read
        await using (var ctx = MakeContext(dbOptions, audit, softDelete, concurrency, opts))
        {
            // Load all entities and filter in-memory to avoid any EF query translation issues
            // with the encrypted property converter.
            var allCustomers = await ctx.Customers.ToListAsync();
            var loaded = allCustomers.FirstOrDefault(c => c.Id == id);

            // Assert
            loaded.Should().NotBeNull();
            loaded!.Email.Should().Be(email, "encrypted property should decrypt to original value");
        }
    }

    [Fact]
    public async Task PlaintextPlantedInEncryptedColumn_FailsClosedOnRead_NamingThePropertyNotTheValue()
    {
        var opts = EnabledOptions();
        var userCtx = TestDbContextFactory.CreateAuthenticatedUserContext(Guid.NewGuid());
        var clock = TestDbContextFactory.CreateClock(DateTimeOffset.UtcNow);
        var svcOpts = TestDbContextFactory.DefaultServiceOptions();
        var audit = new AuditInterceptor(userCtx, clock, svcOpts);
        var softDelete = new SoftDeleteInterceptor(userCtx, clock, svcOpts);
        var concurrency = new ConcurrencyInterceptor();

        var dbOptions = new DbContextOptionsBuilder<EncryptedTestDbContext>()
            .UseSqlite($"DataSource=file:{Guid.NewGuid():N}?mode=memory&cache=shared")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;

        await using var ctx = MakeContext(dbOptions, audit, softDelete, concurrency, opts);
        ctx.Database.EnsureCreated();
        ctx.Customers.Add(new EncryptedTestCustomer(EncryptedTestId.New(), "real@example.com", clock));
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        // Simulate an attacker with database write access replacing the ciphertext with plaintext.
        var entityType = ctx.Model.FindEntityType(typeof(EncryptedTestCustomer))!;
        var table = entityType.GetTableName();
        var column = entityType.FindProperty(nameof(EncryptedTestCustomer.Email))!.GetColumnName();
#pragma warning disable EF1002 // identifiers come from the model, not user input
        await ctx.Database.ExecuteSqlRawAsync($"UPDATE \"{table}\" SET \"{column}\" = 'planted@attacker.example'");
#pragma warning restore EF1002

        var act = async () => await ctx.Customers.ToListAsync();

        var exception = (await act.Should().ThrowAsync<System.Security.Cryptography.CryptographicException>()).Which;
        exception.Message.Should().Contain("EncryptedTestCustomer.Email");
        exception.Message.Should().NotContain("planted@attacker.example");
    }

    [Fact]
    public void PropertyAnnotatedWithEncrypt_HasConverter_In_Model()
    {
        // Arrange
        var opts = EnabledOptions();
        var userCtx = TestDbContextFactory.CreateAuthenticatedUserContext(Guid.NewGuid());
        var clock = TestDbContextFactory.CreateClock(DateTimeOffset.UtcNow);
        var svcOpts = TestDbContextFactory.DefaultServiceOptions();

        var audit = new AuditInterceptor(userCtx, clock, svcOpts);
        var softDelete = new SoftDeleteInterceptor(userCtx, clock, svcOpts);
        var concurrency = new ConcurrencyInterceptor();

        var dbOptions = new DbContextOptionsBuilder<EncryptedTestDbContext>()
            .UseSqlite($"DataSource=file:{Guid.NewGuid():N}?mode=memory&cache=shared")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;

        using var ctx = MakeContext(dbOptions, audit, softDelete, concurrency, opts);

        // Act — inspect model metadata
        var entityType = ctx.Model.FindEntityType(typeof(EncryptedTestCustomer));
        var emailProp = entityType?.FindProperty(nameof(EncryptedTestCustomer.Email));

        // Assert — the converter should be registered
        emailProp.Should().NotBeNull();
        emailProp!.GetValueConverter().Should().BeOfType<EncryptedValueConverter>(
            "EncryptionModelConvention should apply EncryptedValueConverter to .Encrypt()-annotated properties");
    }

    [Fact]
    public void PropertyWithoutEncryptAnnotation_HasNoConverter_In_Model()
    {
        // Arrange
        var opts = EnabledOptions();
        var userCtx = TestDbContextFactory.CreateAuthenticatedUserContext(Guid.NewGuid());
        var clock = TestDbContextFactory.CreateClock(DateTimeOffset.UtcNow);
        var svcOpts = TestDbContextFactory.DefaultServiceOptions();

        var audit = new AuditInterceptor(userCtx, clock, svcOpts);
        var softDelete = new SoftDeleteInterceptor(userCtx, clock, svcOpts);
        var concurrency = new ConcurrencyInterceptor();

        var dbOptions = new DbContextOptionsBuilder<EncryptedTestDbContext>()
            .UseSqlite($"DataSource=file:{Guid.NewGuid():N}?mode=memory&cache=shared")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;

        using var ctx = MakeContext(dbOptions, audit, softDelete, concurrency, opts);

        // Act — the Name property is NOT annotated with .Encrypt()
        var entityType = ctx.Model.FindEntityType(typeof(EncryptedTestCustomer));
        var nameProp = entityType?.FindProperty(nameof(EncryptedTestCustomer.Name));

        // Assert — no EncryptedValueConverter on the Name property (no .Encrypt() annotation)
        nameProp.Should().NotBeNull();
        var nameConverter = nameProp!.GetValueConverter();
        // GetValueConverter returns null for properties without a converter, or returns
        // a non-encryption converter. Either way it must not be EncryptedValueConverter.
        if (nameConverter is not null)
        {
            nameConverter.Should().NotBeOfType<EncryptedValueConverter>(
                "Name property has no .Encrypt() annotation so EncryptedValueConverter must not be applied");
        }
        // null converter is the expected case — no assertion needed
    }
}

// ---------------------------------------------------------------------------
// Test-local entity, ID, configuration, and DbContext for encryption tests
// ---------------------------------------------------------------------------

internal sealed record EncryptedTestId(Guid Value) : StronglyTypedId<Guid>(Value)
{
    public static EncryptedTestId New() => new(Guid.NewGuid());
}

internal sealed class EncryptedTestCustomer : AggregateRoot<EncryptedTestId>
{
    public string Email { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;

    public EncryptedTestCustomer(EncryptedTestId id, string email, IClock clock)
        : base(id, clock)
    {
        Email = email;
        Name = "Test Name";
    }

    protected EncryptedTestCustomer() { } // EF Core path
}

internal sealed class EncryptedTestCustomerConfig : EntityTypeConfigurationBase<EncryptedTestCustomer, EncryptedTestId>
{
    public override void Configure(EntityTypeBuilder<EncryptedTestCustomer> builder)
    {
        base.Configure(builder);
        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
        // Mark Email for encryption using the .Encrypt() extension
        builder.Property(e => e.Email).HasMaxLength(500).Encrypt().IsRequired();
    }
}

internal sealed class EncryptedTestDbContext : SharedKernelDbContext
{
    public DbSet<EncryptedTestCustomer> Customers => Set<EncryptedTestCustomer>();

    public EncryptedTestDbContext(
        DbContextOptions<EncryptedTestDbContext> options,
        AuditInterceptor audit,
        SoftDeleteInterceptor softDelete,
        ConcurrencyInterceptor concurrency,
        IOptionsMonitor<EncryptionOptions>? encryptionOptions = null,
        ISynchronousSymmetricEncryptionService? symmetricEncryptionService = null,
        IEncryptionVersionOverride? versionOverride = null)
        : base(options, audit, softDelete, concurrency, null, encryptionOptions,
               versionOverride, symmetricEncryptionService)
    {
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.ConfigureStronglyTypedId<EncryptedTestId, Guid>();
        base.ConfigureConventions(configurationBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new EncryptedTestCustomerConfig());
        // Do NOT call base.OnModelCreating to avoid scanning whole assembly
    }
}
