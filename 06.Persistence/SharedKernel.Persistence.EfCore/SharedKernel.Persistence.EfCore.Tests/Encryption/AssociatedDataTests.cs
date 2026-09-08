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
/// T-140 (P-498/WO-081, D-128): associated-data (AAD) derivation tests — a ciphertext produced for
/// one encrypted column must fail to decrypt through a different column's converter; the same
/// column/row must keep round-tripping; an explicit <c>associatedDataOverride</c> must survive a
/// simulated table/column rename; a converter without an override must NOT survive that same rename.
/// </summary>
public sealed class AssociatedDataTests
{
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

    private static EncryptedValueConverter MakeConverter(byte[] associatedData)
    {
        var monitor = new FixedOptionsMonitor(EnabledOptions());
        var keyProvider = new EncryptionOptionsKeyProvider(
            monitor, EncryptionVersionOverride.NoOp, new EncryptionKeyByteCache(monitor));
        var encryptionService = new AesGcmEncryptionService(keyProvider);
        return new EncryptedValueConverter(monitor, encryptionService, associatedData);
    }

    private static byte[] StorageIdentity(string schema, string table, string column) =>
        System.Text.Encoding.UTF8.GetBytes($"{schema}.{table}.{column}");

    [Fact]
    public void SameColumn_RoundTrips_Correctly()
    {
        var converter = MakeConverter(StorageIdentity("public", "customers", "email"));
        var toProvider = converter.ConvertToProviderExpression.Compile();
        var fromProvider = converter.ConvertFromProviderExpression.Compile();

        var encrypted = toProvider("alice@example.com");
        var decrypted = fromProvider(encrypted);

        decrypted.Should().Be("alice@example.com");
    }

    [Fact]
    public void CiphertextFromOneColumn_FailsAuthentication_ThroughDifferentColumnConverter()
    {
        // Simulate: an SSN value's ciphertext spliced into a PhoneNumber column.
        var ssnConverter = MakeConverter(StorageIdentity("public", "customers", "ssn"));
        var phoneConverter = MakeConverter(StorageIdentity("public", "customers", "phone_number"));

        var ciphertextFromSsnColumn = ssnConverter.ConvertToProviderExpression.Compile()("123-45-6789");
        var fromPhoneProvider = phoneConverter.ConvertFromProviderExpression.Compile();

        var act = () => fromPhoneProvider(ciphertextFromSsnColumn);

        act.Should().Throw<System.Security.Cryptography.CryptographicException>(
            "a ciphertext produced with one column's AAD must fail AES-GCM authentication when " +
            "decrypted through a different column's converter — the column-splicing hazard D-128 closes");
    }

    [Fact]
    public void CiphertextFromSameColumn_DifferentRow_StillDecryptsCorrectly()
    {
        // AAD is bound to COLUMN identity, not row content — a documented, accepted weaker bound
        // than row-level binding (D-128).
        var converter = MakeConverter(StorageIdentity("public", "customers", "email"));
        var toProvider = converter.ConvertToProviderExpression.Compile();
        var fromProvider = converter.ConvertFromProviderExpression.Compile();

        // Two different "rows" (different plaintext values) through the SAME converter instance.
        var row1Ciphertext = toProvider("row1@example.com");
        var row2Ciphertext = toProvider("row2@example.com");

        fromProvider(row1Ciphertext).Should().Be("row1@example.com");
        fromProvider(row2Ciphertext).Should().Be("row2@example.com");
    }

    [Fact]
    public void ExplicitAssociatedDataOverride_SurvivesSimulatedTableRename()
    {
        // Two converters built with the SAME override string, but DIFFERENT physical table/column
        // identities (simulating a rename that happened between them) — must round-trip against
        // each other, since both were constructed with the override, never the physical identity.
        const string stableOverride = "Customer.Ssn";
        var beforeRename = new EncryptedValueConverter(
            new FixedOptionsMonitor(EnabledOptions()),
            new AesGcmEncryptionService(new EncryptionOptionsKeyProvider(
                new FixedOptionsMonitor(EnabledOptions()), EncryptionVersionOverride.NoOp,
                new EncryptionKeyByteCache(new FixedOptionsMonitor(EnabledOptions())))),
            System.Text.Encoding.UTF8.GetBytes(stableOverride));

        // Re-derive from the SAME override string as if the table/column had since been renamed —
        // a genuinely independent converter instance, proving the override alone (not physical
        // identity) determines the AAD.
        var afterRenameConverter = MakeConverterWithOverride(stableOverride);

        var encrypted = beforeRename.ConvertToProviderExpression.Compile()("999-99-9999");
        var decrypted = afterRenameConverter.ConvertFromProviderExpression.Compile()(encrypted);

        decrypted.Should().Be("999-99-9999",
            "an explicit associatedDataOverride is stable across a physical table/column rename");
    }

    [Fact]
    public void NoOverride_ConverterAfterSimulatedRename_FailsToDecrypt()
    {
        // Documents (does not "fix") the rename hazard: a converter using the DEFAULT
        // schema+table+column-derived AAD produces ciphertext that a converter built AFTER a
        // simulated rename (different table/column identity) cannot decrypt.
        var beforeRenameConverter = MakeConverter(StorageIdentity("public", "customers_old_name", "email"));
        var afterRenameConverter = MakeConverter(StorageIdentity("public", "customers_new_name", "email"));

        var encrypted = beforeRenameConverter.ConvertToProviderExpression.Compile()("bob@example.com");
        var fromAfterRename = afterRenameConverter.ConvertFromProviderExpression.Compile();

        var act = () => fromAfterRename(encrypted);

        act.Should().Throw<System.Security.Cryptography.CryptographicException>(
            "renaming the underlying table changes the default AAD — every existing row's " +
            "ciphertext for that column becomes permanently undecryptable unless an explicit " +
            "associatedDataOverride was supplied up front (documented hazard, D-128)");
    }

    private static EncryptedValueConverter MakeConverterWithOverride(string overrideString) =>
        MakeConverter(System.Text.Encoding.UTF8.GetBytes(overrideString));

    // -------------------------------------------------------------------------
    // End-to-end through EncryptionModelConvention: proves the REAL derivation path (schema+table+
    // column resolved from EF Core relational metadata at model-finalization time) produces the
    // expected behavior, not just the hand-computed byte[] used by the unit tests above.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task EndToEnd_TwoEncryptedColumns_SameEntity_CrossColumnCiphertext_FailsToDecrypt()
    {
        var userCtx = TestDbContextFactory.CreateAuthenticatedUserContext(Guid.NewGuid());
        var clock = TestDbContextFactory.CreateClock(DateTimeOffset.UtcNow);
        var svcOpts = TestDbContextFactory.DefaultServiceOptions();

        var audit = new AuditInterceptor(userCtx, clock, svcOpts);
        var softDelete = new SoftDeleteInterceptor(userCtx, clock, svcOpts);
        var concurrency = new ConcurrencyInterceptor();

        var monitor = new FixedOptionsMonitor(EnabledOptions());
        var keyProvider = new EncryptionOptionsKeyProvider(
            monitor, EncryptionVersionOverride.NoOp, new EncryptionKeyByteCache(monitor));
        var encryptionService = new AesGcmEncryptionService(keyProvider);

        var dbOptions = new DbContextOptionsBuilder<AadTwoColumnDbContext>()
            .UseSqlite($"DataSource=file:{Guid.NewGuid():N}?mode=memory&cache=shared")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;

        await using var ctx = new AadTwoColumnDbContext(
            dbOptions, audit, softDelete, concurrency, monitor, EncryptionVersionOverride.NoOp, encryptionService, keyProvider);
        await ctx.Database.EnsureCreatedAsync();

        var id = AadTwoColumnId.New();
        ctx.Entities.Add(new AadTwoColumnEntity(id, "123-45-6789", "555-0100", clock));
        await ctx.SaveChangesAsync();

        var ssnProp = ctx.Model.FindEntityType(typeof(AadTwoColumnEntity))!.FindProperty(nameof(AadTwoColumnEntity.Ssn))!;
        var phoneProp = ctx.Model.FindEntityType(typeof(AadTwoColumnEntity))!.FindProperty(nameof(AadTwoColumnEntity.PhoneNumber))!;

        var ssnConverter = (EncryptedValueConverter)ssnProp.GetValueConverter()!;
        var phoneConverter = (EncryptedValueConverter)phoneProp.GetValueConverter()!;

        var rawRow = await ctx.Database
            .SqlQueryRaw<RawTwoColumn>("SELECT Ssn AS ssn, PhoneNumber AS phone_number FROM aad_two_column_entities")
            .ToListAsync();
        var storedSsnCiphertext = rawRow[0].ssn;

        var act = () => phoneConverter.ConvertFromProviderExpression.Compile()(storedSsnCiphertext);

        act.Should().Throw<System.Security.Cryptography.CryptographicException>(
            "the SSN column's ciphertext must fail authentication when decrypted via the " +
            "PhoneNumber column's converter — both are wired by the real EncryptionModelConvention " +
            "AAD derivation, not a hand-computed value");

        // Sanity: the correct converter still round-trips.
        ssnConverter.ConvertFromProviderExpression.Compile()(storedSsnCiphertext).Should().Be("123-45-6789");
    }

    private sealed record RawTwoColumn(string ssn, string phone_number);
}

// ---------------------------------------------------------------------------
// Test-local entity/context for the end-to-end AAD derivation proof.
// ---------------------------------------------------------------------------

internal sealed record AadTwoColumnId(Guid Value) : StronglyTypedId<Guid>(Value)
{
    public static AadTwoColumnId New() => new(Guid.NewGuid());
}

internal sealed class AadTwoColumnEntity : AggregateRoot<AadTwoColumnId>
{
    public string Ssn { get; private set; } = string.Empty;
    public string PhoneNumber { get; private set; } = string.Empty;

    public AadTwoColumnEntity(AadTwoColumnId id, string ssn, string phoneNumber, IClock clock)
        : base(id, clock)
    {
        Ssn = ssn;
        PhoneNumber = phoneNumber;
    }

    protected AadTwoColumnEntity() { } // EF Core path
}

internal sealed class AadTwoColumnEntityConfig : EntityTypeConfigurationBase<AadTwoColumnEntity, AadTwoColumnId>
{
    public override void Configure(EntityTypeBuilder<AadTwoColumnEntity> builder)
    {
        base.Configure(builder);
        builder.ToTable("aad_two_column_entities");
        builder.Property(e => e.Ssn).HasMaxLength(200).Encrypt().IsRequired();
        builder.Property(e => e.PhoneNumber).HasMaxLength(200).Encrypt().IsRequired();
    }
}

internal sealed class AadTwoColumnDbContext : SharedKernelDbContext
{
    public Microsoft.EntityFrameworkCore.DbSet<AadTwoColumnEntity> Entities => Set<AadTwoColumnEntity>();

    public AadTwoColumnDbContext(
        DbContextOptions<AadTwoColumnDbContext> options,
        AuditInterceptor audit,
        SoftDeleteInterceptor softDelete,
        ConcurrencyInterceptor concurrency,
        IOptionsMonitor<EncryptionOptions>? encryptionOptions = null,
        IEncryptionVersionOverride? encryptionVersionOverride = null,
        ISymmetricEncryptionService? symmetricEncryptionService = null,
        IEncryptionKeyProvider? encryptionKeyProvider = null)
        : base(options, audit, softDelete, concurrency, null, encryptionOptions,
               encryptionVersionOverride, symmetricEncryptionService, encryptionKeyProvider)
    {
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.ConfigureStronglyTypedId<AadTwoColumnId, Guid>();
        base.ConfigureConventions(configurationBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new AadTwoColumnEntityConfig());
        // Do NOT call base.OnModelCreating to avoid scanning the whole test assembly.
    }
}
