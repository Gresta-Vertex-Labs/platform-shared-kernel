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
using SharedKernel.Primitives.Clocks;
using SharedKernel.Testing.Cryptography;

namespace SharedKernel.Persistence.EfCore.Tests.Encryption;

/// <summary>
/// <c>.WithExternalEncryptionKeyProvider&lt;TProvider&gt;()</c> end-to-end tests — an asynchronous-only
/// (KMS-shaped) key provider driving the synchronous EF Core value converter through pre-warming, exercised
/// through the REAL DI builder pipeline (not hand-constructed converters).
/// </summary>
public sealed class WithExternalEncryptionKeyProviderTests
{
    private static async Task<(ServiceProvider Provider, FakeRemoteEncryptionKeyProvider External)> BuildHostAsync()
    {
        var services = new ServiceCollection();

        // FakeRemoteEncryptionKeyProvider only completes asynchronously and deliberately does not implement
        // ISynchronousEncryptionKeyProvider — the shape of a real KMS-backed provider. Registered as itself,
        // exactly as a consumer registers the provider type it passes to WithExternalEncryptionKeyProvider.
        var external = new FakeRemoteEncryptionKeyProvider("v1");
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
            .WithExternalEncryptionKeyProvider<FakeRemoteEncryptionKeyProvider>()
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
    public async Task EncryptAndReadRow_WithAsyncOnlyProvider_RoundTripsThroughPreWarming()
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
            var raw = await ctx.Database
                .SqlQueryRaw<string>("SELECT Secret AS Value FROM external_provider_test_entities")
                .ToListAsync();
            EncryptedPayload.TryParse(raw.Single(), out var payload).Should().BeTrue(
                "the column stores the canonical EncryptedPayload encoding");
            payload!.KeyId.Should().Be("v1", "the payload records the external provider's current key id");

            var loaded = await ctx.Entities.FirstOrDefaultAsync(e => e.Id == id);
            loaded.Should().NotBeNull();
            loaded!.Secret.Should().Be("secret-value");
        }

        external.CurrentKeyCallCount.Should().Be(1,
            "the external provider is consulted once, asynchronously, at warm-up — never per row");

        await provider.DisposeAsync();
    }

    [Fact]
    public async Task ResolvedEncryptionService_IsSynchronous_OverThePreWarmedProvider()
    {
        var (provider, _) = await BuildHostAsync();

        var keyProvider = provider.GetRequiredKeyedService<ISynchronousEncryptionKeyProvider>(
            PersistenceEncryptionKeys.EncryptionKeyProviderKey);
        var service = provider.GetRequiredKeyedService<ISynchronousSymmetricEncryptionService>(
            PersistenceEncryptionKeys.SymmetricEncryptionServiceKey);

        keyProvider.Should().BeOfType<PreWarmedEncryptionKeyProvider>();
        service.Should().BeOfType<SynchronousAesGcmEncryptionService>();

        var associatedData = "public.t.c"u8.ToArray();
        var payload = service.Encrypt("hello"u8, associatedData);
        payload.KeyId.Should().Be("v1");
        service.Decrypt(payload, associatedData).Value.Should().Equal("hello"u8.ToArray());

        await provider.DisposeAsync();
    }

    [Fact]
    public async Task UnregisteredProviderType_FailsHostStartup()
    {
        var services = new ServiceCollection();
        services
            .AddSharedKernelEfCore<ExternalProviderTestDbContext>(opts =>
                opts.UseSqlite("DataSource=:memory:")
                    .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
            .WithEncryption(enc =>
            {
                enc.Enabled = true;
                enc.CurrentVersion = "v1";
                enc.Keys["v1"] = Convert.ToBase64String(new byte[32]);
            })
            .WithExternalEncryptionKeyProvider<FakeRemoteEncryptionKeyProvider>() // never registered
            .Build();

        await using var provider = services.BuildServiceProvider();

        var act = async () =>
        {
            foreach (var hostedService in provider.GetServices<IHostedService>())
            {
                await hostedService.StartAsync(CancellationToken.None);
            }
        };

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*{nameof(FakeRemoteEncryptionKeyProvider)}*");
    }

    [Fact]
    public async Task DecryptOlderExplicitlyPreWarmedHistoricalVersion_Succeeds()
    {
        var (provider, external) = await BuildHostAsync();
        external.AddKey("v2");

        var preWarmed = provider.GetRequiredService<PreWarmedEncryptionKeyProvider>();
        var versionOverride = provider.GetRequiredService<IEncryptionVersionOverride>();

        // Explicitly pre-warm the historical version — the sanctioned path for decrypting an older
        // version, since the interceptor hooks only ever warm the CURRENT version.
        await preWarmed.WarmVersionAsync("v2");

        var factory = provider.GetRequiredService<IDbContextFactory<ExternalProviderTestDbContext>>();
        await using (var ctx = await factory.CreateDbContextAsync())
        {
            await ctx.Database.EnsureCreatedAsync();
        }

        var id = ExternalProviderTestId.New();

        // Encrypt with v2 through the override precedence rule.
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

        await provider.DisposeAsync();
    }

    [Fact]
    public async Task DecryptUnwarmedHistoricalVersion_FailsWithoutBlocking_ThenWarmsInBackground()
    {
        var (provider, external) = await BuildHostAsync();
        // "v3" exists on the external provider but is NEVER warmed on this process.
        external.AddKey("v3");

        var factory = provider.GetRequiredService<IDbContextFactory<ExternalProviderTestDbContext>>();
        await using var ctx = await factory.CreateDbContextAsync();
        await ctx.Database.EnsureCreatedAsync();

        var converter = (EncryptedValueConverter)ctx.Model
            .FindEntityType(typeof(ExternalProviderTestEntity))!
            .FindProperty(nameof(ExternalProviderTestEntity.Secret))!
            .GetValueConverter()!;

        var callsBefore = external.KeyCallCount;

        // A well-formed payload claiming key id "v3".
        var storedWithV3 = new EncryptedPayload("v3", new byte[12], new byte[8], new byte[16]).ToString();
        var fromProvider = converter.ConvertFromProviderExpression.Compile();

        var act = () => fromProvider(storedWithV3);

        act.Should().Throw<EncryptionKeyNotFoundException>()
            .Which.Version.Should().Be("v3");

        // The miss never awaited the key service; it scheduled a background warm of "v3".
        var preWarmed = provider.GetRequiredService<PreWarmedEncryptionKeyProvider>();
        await preWarmed.WaitForPendingWarmsAsync();
        external.KeyCallCount.Should().Be(callsBefore + 1, "exactly one background lookup of the missing key id");

        // v3 is now warm, so this forged all-zero payload reaches authentication and fails closed.
        act.Should().Throw<System.Security.Cryptography.CryptographicException>();

        await provider.DisposeAsync();
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
        ISynchronousSymmetricEncryptionService? symmetricEncryptionService = null)
        : base(options, auditInterceptor, softDeleteInterceptor, concurrencyInterceptor,
               additionalInterceptors, encryptionOptions, encryptionVersionOverride,
               symmetricEncryptionService)
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
