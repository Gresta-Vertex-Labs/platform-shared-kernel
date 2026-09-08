using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Domain.Aggregates;
using SharedKernel.Domain.StronglyTypedIds;
using SharedKernel.Persistence.EfCore.Configurations;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Conversions;
using SharedKernel.Persistence.EfCore.Encryption;
using SharedKernel.Persistence.EfCore.Extensions;
using SharedKernel.Persistence.EfCore.Interceptors;
using SharedKernel.Persistence.EfCore.Options;
using SharedKernel.Persistence.EfCore.Tests.TestFixtures;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Testing.Cryptography;

namespace SharedKernel.Persistence.EfCore.Tests.Encryption;

/// <summary>
/// T-145 (P-498/WO-081): <c>.WithExternalEncryptionKeyProvider&lt;TProvider&gt;()</c> end-to-end
/// tests — the KMS-backed opt-in path, exercised through the REAL DI builder pipeline (not
/// hand-constructed converters).
/// </summary>
public sealed class WithExternalEncryptionKeyProviderTests
{
    // A call-counting external IEncryptionKeyProvider — genuinely async (mirrors
    // 16.Testing's FakeRemoteEncryptionKeyProvider shape), deliberately never implementing
    // ISynchronousEncryptionKeyProvider, with call counts exposed for the non-blocking proof below.
    private sealed class CallCountingExternalProvider : IEncryptionKeyProvider
    {
        private readonly Dictionary<string, byte[]> _keys = new();

        public string CurrentKeyId { get; set; } = "v1";
        public int GetKeyAsyncCallCount { get; private set; }
        public int GetCurrentKeyAsyncCallCount { get; private set; }

        public void AddKey(string keyId, byte fill)
        {
            var material = new byte[32];
            Array.Fill(material, fill);
            _keys[keyId] = material;
        }

        public async ValueTask<CryptographicKey> GetCurrentKeyAsync(CancellationToken ct = default)
        {
            await Task.Yield(); // genuinely asynchronous, like a real KMS round trip
            GetCurrentKeyAsyncCallCount++;
            return new CryptographicKey(CurrentKeyId, _keys[CurrentKeyId]);
        }

        public async ValueTask<CryptographicKey?> GetKeyAsync(string keyId, CancellationToken ct = default)
        {
            await Task.Yield();
            GetKeyAsyncCallCount++;
            return _keys.TryGetValue(keyId, out var material) ? new CryptographicKey(keyId, material) : null;
        }
    }

    private static async Task<(ServiceProvider Provider, CallCountingExternalProvider External)> BuildHostAsync()
    {
        var services = new ServiceCollection();

        var external = new CallCountingExternalProvider();
        external.AddKey("v1", 0x11);
        // Registered UNKEYED — mirroring a consumer's own general-purpose provider registration
        // (e.g. 13.ServiceDefaults' AddSharedKernelKeyVaultKeyProvider). WithExternalEncryptionKeyProvider
        // resolves it from this ambient slot; this package never registers it there itself.
        services.AddSingleton(external);

        services
            .AddSharedKernelEfCore<ExternalProviderTestDbContext>(opts =>
                opts.UseSqlite($"DataSource=file:{Guid.NewGuid():N}?mode=memory&cache=shared")
                    .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
            .WithEncryption(enc =>
            {
                enc.Enabled = true;
                enc.CurrentVersion = "v1";
                enc.Keys["v1"] = Convert.ToBase64String(new byte[32]); // unused — external provider supplies real material
            })
            .WithExternalEncryptionKeyProvider<CallCountingExternalProvider>()
            .WithDbContextFactory()
            .Build();

        var provider = services.BuildServiceProvider();

        // Simulate real host startup — invoke every registered IHostedService's StartAsync exactly
        // as the Generic Host does before accepting traffic, so EncryptionKeyPreWarmingHostedService
        // warms the current key before any DbContext operation below.
        foreach (var hostedService in provider.GetServices<IHostedService>())
        {
            await hostedService.StartAsync(CancellationToken.None);
        }

        return (provider, external);
    }

    [Fact]
    public async Task EncryptNewRow_CurrentKeyAlreadyWarmedByHostedService_Succeeds()
    {
        var (provider, external) = await BuildHostAsync();
        await using var scope = provider.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ExternalProviderTestDbContext>>();

        await using (var ctx = await factory.CreateDbContextAsync())
        {
            await ctx.Database.EnsureCreatedAsync();
        }

        var id = ExternalProviderTestId.New();
        await using (var ctx = await factory.CreateDbContextAsync())
        {
            ctx.Entities.Add(new ExternalProviderTestEntity(id, "secret-value", new SystemClock()));
            var act = async () => await ctx.SaveChangesAsync();
            await act.Should().NotThrowAsync(
                "the hosted service warmed the current key at boot, so encrypting a new row never " +
                "hits PreWarmedEncryptionKeyProvider's unwarmed-throw branch");
        }

        await using (var ctx = await factory.CreateDbContextAsync())
        {
            var loaded = await ctx.Entities.FirstOrDefaultAsync(e => e.Id == id);
            loaded.Should().NotBeNull();
            loaded!.Secret.Should().Be("secret-value");
        }

        provider.Dispose();
    }

    [Fact]
    public async Task DecryptOlderExplicitlyPreWarmedHistoricalVersion_Succeeds()
    {
        var (provider, external) = await BuildHostAsync();
        external.AddKey("v2", 0x22);

        var preWarmed = provider.GetRequiredService<PreWarmedEncryptionKeyProvider>();
        var versionOverride = provider.GetRequiredService<IEncryptionVersionOverride>();

        // Explicitly pre-warm the historical version — proving the sanctioned path for decrypting
        // an older version rather than relying on the coarse ReaderExecutingAsync/SavingChangesAsync
        // hooks (which only ever warm the CURRENT version).
        await preWarmed.WarmVersionAsync("v2");

        var factory = provider.GetRequiredService<IDbContextFactory<ExternalProviderTestDbContext>>();
        await using (var ctx = await factory.CreateDbContextAsync())
        {
            await ctx.Database.EnsureCreatedAsync();
        }

        var id = ExternalProviderTestId.New();

        // Also warm v2 as the CURRENT tag momentarily so GetCurrentKeyAsync (used by encrypt) can
        // resolve it via the override precedence rule.
        versionOverride.OverrideVersion = "v2";
        try
        {
            await using var ctx = await factory.CreateDbContextAsync();
            ctx.Entities.Add(new ExternalProviderTestEntity(id, "rotated-secret", new SystemClock()));
            await ctx.SaveChangesAsync();
        }
        finally
        {
            versionOverride.OverrideVersion = null;
        }

        await using var readCtx = await factory.CreateDbContextAsync();
        var loaded = await readCtx.Entities.FirstOrDefaultAsync(e => e.Id == id);

        loaded.Should().NotBeNull();
        loaded!.Secret.Should().Be("rotated-secret",
            "the explicitly pre-warmed v2 version must decrypt correctly");

        provider.Dispose();
    }

    [Fact]
    public async Task DecryptUnwarmedHistoricalVersion_SurfacesExistingEncryptionKeyNotFoundException_NeverTouchesExternalProvider()
    {
        var (provider, external) = await BuildHostAsync();
        // "v3" exists on the external provider but is NEVER warmed on this process.
        external.AddKey("v3", 0x33);

        var factory = provider.GetRequiredService<IDbContextFactory<ExternalProviderTestDbContext>>();
        await using var ctx = await factory.CreateDbContextAsync();
        await ctx.Database.EnsureCreatedAsync();

        var converter = (EncryptedValueConverter)ctx.Model
            .FindEntityType(typeof(ExternalProviderTestEntity))!
            .FindProperty(nameof(ExternalProviderTestEntity.Secret))!
            .GetValueConverter()!;

        var callsBefore = external.GetKeyAsyncCallCount;

        // A ciphertext claiming version "v3" — well-formed length so the converter's own length
        // guard doesn't short-circuit before ever reaching Decrypt.
        var storedWithV3 = "vv3:" + Convert.ToBase64String(new byte[28]);
        var fromProvider = converter.ConvertFromProviderExpression.Compile();

        var act = () => fromProvider(storedWithV3);

        act.Should().Throw<EncryptionKeyNotFoundException>()
            .Which.Version.Should().Be("v3");

        external.GetKeyAsyncCallCount.Should().Be(callsBefore,
            "PreWarmedEncryptionKeyProvider.GetKeyAsync fails closed on a cache miss — it never " +
            "touches the external provider, so this failure is non-blocking, not a hidden KMS call");

        provider.Dispose();
    }
}

// ---------------------------------------------------------------------------
// Test-local entity/context for the external-provider end-to-end tests.
// ---------------------------------------------------------------------------

internal sealed record ExternalProviderTestId(Guid Value) : StronglyTypedId<Guid>(Value)
{
    public static ExternalProviderTestId New() => new(Guid.NewGuid());
}

internal sealed class ExternalProviderTestEntity : AggregateRoot<ExternalProviderTestId>
{
    public string Secret { get; private set; } = string.Empty;

    public ExternalProviderTestEntity(ExternalProviderTestId id, string secret, IClock clock) : base(id, clock)
    {
        Secret = secret;
    }

    protected ExternalProviderTestEntity() { } // EF Core path
}

internal sealed class ExternalProviderTestEntityConfig
    : EntityTypeConfigurationBase<ExternalProviderTestEntity, ExternalProviderTestId>
{
    public override void Configure(EntityTypeBuilder<ExternalProviderTestEntity> builder)
    {
        base.Configure(builder);
        builder.ToTable("external_provider_test_entities");
        builder.Property(e => e.Secret).HasMaxLength(500).Encrypt().IsRequired();
    }
}

internal sealed class ExternalProviderTestDbContext : SharedKernelDbContext
{
    public DbSet<ExternalProviderTestEntity> Entities => Set<ExternalProviderTestEntity>();

    public ExternalProviderTestDbContext(
        DbContextOptions<ExternalProviderTestDbContext> options,
        AuditInterceptor auditInterceptor,
        SoftDeleteInterceptor softDeleteInterceptor,
        ConcurrencyInterceptor concurrencyInterceptor,
        IEnumerable<Microsoft.EntityFrameworkCore.Diagnostics.ISaveChangesInterceptor>? additionalInterceptors = null,
        Microsoft.Extensions.Options.IOptionsMonitor<EncryptionOptions>? encryptionOptions = null,
        IEncryptionVersionOverride? encryptionVersionOverride = null,
        ISymmetricEncryptionService? symmetricEncryptionService = null,
        IEncryptionKeyProvider? encryptionKeyProvider = null)
        : base(options, auditInterceptor, softDeleteInterceptor, concurrencyInterceptor,
               additionalInterceptors, encryptionOptions, encryptionVersionOverride,
               symmetricEncryptionService, encryptionKeyProvider)
    {
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.ConfigureStronglyTypedId<ExternalProviderTestId, Guid>();
        base.ConfigureConventions(configurationBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new ExternalProviderTestEntityConfig());
        // Do NOT call base.OnModelCreating to avoid scanning the whole test assembly.
    }
}
