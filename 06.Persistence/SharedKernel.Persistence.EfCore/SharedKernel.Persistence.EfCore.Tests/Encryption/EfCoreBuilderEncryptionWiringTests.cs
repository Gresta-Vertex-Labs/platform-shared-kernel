using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Persistence.EfCore.Encryption;
using SharedKernel.Persistence.EfCore.Encryption.Rotation;
using SharedKernel.Persistence.EfCore.Extensions;
using SharedKernel.Persistence.EfCore.Options;
using SharedKernel.Persistence.EfCore.Tests.TestFixtures;

namespace SharedKernel.Persistence.EfCore.Tests.Encryption;

/// <summary>
/// T-46: EfCorePersistenceBuilder encryption wiring tests (P-113).
/// </summary>
public sealed class EfCoreBuilderEncryptionWiringTests
{
    private static string ValidBase64Key()
        => Convert.ToBase64String(new byte[32]);

    // -------------------------------------------------------------------------
    // IEncryptionRotationJob is NOT registered when WithEncryption is NOT called
    // -------------------------------------------------------------------------

    [Fact]
    public void WithoutEncryption_IEncryptionRotationJob_IsNotRegistered()
    {
        var services = new ServiceCollection();
        services
            .AddSharedKernelEfCore<TestDbContext>(opts => opts.UseSqlite("DataSource=:memory:").ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
            .Build();

        var provider = services.BuildServiceProvider();

        // IEncryptionRotationJob must not resolve — EncryptionRotationService<TContext> is abstract
        // and is only registered as scoped when WithEncryption() is called.
        var descriptor = services.FirstOrDefault(sd =>
            sd.ServiceType == typeof(IEncryptionRotationJob));

        descriptor.Should().BeNull(
            "IEncryptionRotationJob must NOT be registered when .WithEncryption() is not called");
    }

    // -------------------------------------------------------------------------
    // WithEncryption registers IEncryptionVersionOverride singleton
    // -------------------------------------------------------------------------

    [Fact]
    public void WithEncryption_RegistersIEncryptionVersionOverride_AsSingleton()
    {
        var services = new ServiceCollection();
        services
            .AddSharedKernelEfCore<TestDbContext>(opts => opts.UseSqlite("DataSource=:memory:").ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
            .WithEncryption(enc =>
            {
                enc.Enabled = true;
                enc.CurrentVersion = "v1";
                enc.Keys["v1"] = ValidBase64Key();
            })
            .Build();

        var provider = services.BuildServiceProvider();

        var override1 = provider.GetService<IEncryptionVersionOverride>();
        var override2 = provider.GetService<IEncryptionVersionOverride>();

        override1.Should().NotBeNull("IEncryptionVersionOverride must be registered when WithEncryption is called");
        override1.Should().BeSameAs(override2, "IEncryptionVersionOverride must be a singleton");
    }

    // -------------------------------------------------------------------------
    // WithEncryption registers EncryptedEntityBatchProcessorRegistry as singleton
    // -------------------------------------------------------------------------

    [Fact]
    public void WithEncryption_RegistersEncryptedEntityBatchProcessorRegistry_AsSingleton()
    {
        var services = new ServiceCollection();
        services
            .AddSharedKernelEfCore<TestDbContext>(opts => opts.UseSqlite("DataSource=:memory:").ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
            .WithEncryption(enc =>
            {
                enc.Enabled = true;
                enc.CurrentVersion = "v1";
                enc.Keys["v1"] = ValidBase64Key();
            })
            .Build();

        var descriptor = services.FirstOrDefault(sd =>
            sd.ServiceType == typeof(EncryptedEntityBatchProcessorRegistry<TestDbContext>));

        descriptor.Should().NotBeNull("EncryptedEntityBatchProcessorRegistry must be registered when WithEncryption is called");
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Singleton, "registry is populated once at startup and immutable");
    }

    // -------------------------------------------------------------------------
    // WithEncryption forces WithDbContextFactory registration
    // -------------------------------------------------------------------------

    [Fact]
    public void WithEncryption_WithoutExplicitDbContextFactory_StillRegistersFactory()
    {
        var services = new ServiceCollection();
        services
            .AddSharedKernelEfCore<TestDbContext>(opts => opts.UseSqlite("DataSource=:memory:").ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
            .WithEncryption(enc =>
            {
                enc.Enabled = true;
                enc.CurrentVersion = "v1";
                enc.Keys["v1"] = ValidBase64Key();
            })
            .Build(); // no .WithDbContextFactory() called explicitly

        var descriptor = services.FirstOrDefault(sd =>
            sd.ServiceType == typeof(IDbContextFactory<TestDbContext>));

        descriptor.Should().NotBeNull(
            "WithEncryption must auto-register IDbContextFactory<TContext> for EncryptionRotationService");
    }

    // -------------------------------------------------------------------------
    // WithServiceName sets the audit fallback used by AuditInterceptor (T-43 backward compat)
    // -------------------------------------------------------------------------

    [Fact]
    public void WithServiceName_RegistersPersistenceServiceOptions_WithCorrectServiceName()
    {
        const string serviceName = "test-service";
        var services = new ServiceCollection();
        services
            .AddSharedKernelEfCore<TestDbContext>(opts => opts.UseSqlite("DataSource=:memory:").ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
            .WithServiceName(serviceName)
            .Build();

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<PersistenceServiceOptions>>();

        options.Value.ServiceName.Should().Be(serviceName,
            "WithServiceName registers PersistenceServiceOptions with the supplied service name");
    }

    [Fact]
    public void WithoutServiceName_DefaultServiceName_IsSystem()
    {
        var services = new ServiceCollection();
        services
            .AddSharedKernelEfCore<TestDbContext>(opts => opts.UseSqlite("DataSource=:memory:").ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
            .Build();

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<PersistenceServiceOptions>>();

        options.Value.ServiceName.Should().Be("system",
            "default PersistenceServiceOptions.ServiceName must be 'system' for backward compatibility");
    }

    // -------------------------------------------------------------------------
    // T-146 (P-498/WO-081, D-131): keyed-DI structural isolation — an unrelated ambient (unkeyed)
    // IEncryptionKeyProvider registration must have ZERO effect on .WithEncryption()'s own resolved
    // provider, in either mode.
    // -------------------------------------------------------------------------

    // A stand-in for an unrelated general-purpose provider a consumer might separately register
    // unkeyed — e.g. simulating 13.ServiceDefaults' AddSharedKernelKeyVaultKeyProvider, or a plain
    // AddSharedKernelCryptography() call paired with the consumer's own IEncryptionKeyProvider.
    private sealed class UnrelatedAmbientProvider : IEncryptionKeyProvider
    {
        public ValueTask<CryptographicKey> GetCurrentKeyAsync(CancellationToken ct = default) =>
            throw new InvalidOperationException(
                "UnrelatedAmbientProvider must never be reached by .WithEncryption()'s own " +
                "persistence-scoped pipeline — if this throws during a test, the keyed-DI " +
                "isolation this phase adds has regressed.");

        public ValueTask<CryptographicKey?> GetKeyAsync(string keyId, CancellationToken ct = default) =>
            throw new InvalidOperationException(
                "UnrelatedAmbientProvider must never be reached by .WithEncryption()'s own " +
                "persistence-scoped pipeline — if this throws during a test, the keyed-DI " +
                "isolation this phase adds has regressed.");
    }

    [Fact]
    public void ConfigBackedDefault_UnrelatedUnkeyedProvider_HasZeroEffect_OnResolvedKeyedProvider()
    {
        var services = new ServiceCollection();

        // Simulates an unrelated registration elsewhere in the SAME container — e.g.
        // 13.ServiceDefaults' AddSharedKernelKeyVaultKeyProvider — registered BEFORE .WithEncryption().
        services.AddSingleton<IEncryptionKeyProvider, UnrelatedAmbientProvider>();
        services.AddSingleton<ISymmetricEncryptionService>(sp =>
            new AesGcmEncryptionService(sp.GetRequiredService<IEncryptionKeyProvider>()));

        services
            .AddSharedKernelEfCore<TestDbContext>(opts => opts.UseSqlite("DataSource=:memory:").ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
            .WithEncryption(enc =>
            {
                enc.Enabled = true;
                enc.CurrentVersion = "v1";
                enc.Keys["v1"] = ValidBase64Key();
            })
            .Build();

        var provider = services.BuildServiceProvider();

        var resolvedKeyProvider = provider.GetRequiredKeyedService<IEncryptionKeyProvider>(
            PersistenceEncryptionKeys.EncryptionKeyProviderKey);

        resolvedKeyProvider.Should().BeOfType<EncryptionOptionsKeyProvider>(
            "the config-backed default must resolve EncryptionOptionsKeyProvider under this " +
            "package's own keyed-DI slot, completely unaffected by the unrelated unkeyed " +
            "registration made elsewhere in the same container");

        // The ambient unkeyed slot itself is left exactly as the consumer registered it — this
        // package never touches or overwrites it.
        provider.GetRequiredService<IEncryptionKeyProvider>().Should().BeOfType<UnrelatedAmbientProvider>();
    }

    [Fact]
    public void ExternalProviderMode_UnrelatedUnkeyedProvider_HasZeroEffect_OnResolvedKeyedProvider()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IEncryptionKeyProvider, UnrelatedAmbientProvider>();

        // The consumer's OWN unkeyed registration of the type WithExternalEncryptionKeyProvider will
        // resolve — deliberately a DIFFERENT concrete type than UnrelatedAmbientProvider, proving
        // the external path resolves ITS type-argument specifically, not "whatever IEncryptionKeyProvider
        // happens to be ambient."
        services.AddSingleton<FakeExternalKmsProvider>();

        services
            .AddSharedKernelEfCore<TestDbContext>(opts => opts.UseSqlite("DataSource=:memory:").ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
            .WithEncryption(enc =>
            {
                enc.Enabled = true;
                enc.CurrentVersion = "v1";
                enc.Keys["v1"] = ValidBase64Key();
            })
            .WithExternalEncryptionKeyProvider<FakeExternalKmsProvider>()
            .Build();

        var provider = services.BuildServiceProvider();

        var resolvedKeyProvider = provider.GetRequiredKeyedService<IEncryptionKeyProvider>(
            PersistenceEncryptionKeys.EncryptionKeyProviderKey);

        resolvedKeyProvider.Should().BeOfType<PreWarmedEncryptionKeyProvider>(
            "WithExternalEncryptionKeyProvider<TProvider>() must resolve under the SAME keyed slot, " +
            "superseding the config-backed default's registration (last-registered-wins) — the " +
            "unrelated ambient UnrelatedAmbientProvider registration has zero effect either way");

        ((PreWarmedEncryptionKeyProvider)resolvedKeyProvider).Inner.Should().BeOfType<FakeExternalKmsProvider>(
            "the external mode must wrap the explicitly type-argument-selected TProvider, never the " +
            "unrelated ambient registration");
    }

    // A second, distinct fake IEncryptionKeyProvider — genuinely different from UnrelatedAmbientProvider —
    // standing in for a real KMS-backed provider a consumer registers for WithExternalEncryptionKeyProvider.
    private sealed class FakeExternalKmsProvider : IEncryptionKeyProvider
    {
        public ValueTask<CryptographicKey> GetCurrentKeyAsync(CancellationToken ct = default) =>
            new(new CryptographicKey("v1", new byte[32]));

        public ValueTask<CryptographicKey?> GetKeyAsync(string keyId, CancellationToken ct = default) =>
            new((CryptographicKey?)null);
    }
}
