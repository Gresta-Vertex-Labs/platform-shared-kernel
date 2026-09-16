using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Persistence.EfCore.Encryption;
using SharedKernel.Persistence.EfCore.Encryption.Rotation;
using SharedKernel.Persistence.EfCore.Extensions;
using SharedKernel.Persistence.EfCore.Options;
using SharedKernel.Persistence.EfCore.Tests.TestFixtures;
using SharedKernel.Testing.Cryptography;

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
    // WithEncryption builds a synchronous encryption service over a synchronous key provider,
    // under this package's own keyed-DI slots.
    // -------------------------------------------------------------------------

    [Fact]
    public void WithEncryption_RegistersSynchronousServiceOverConfigBackedProvider_AsSingletons()
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

        using var provider = services.BuildServiceProvider();

        var keyProvider = provider.GetRequiredKeyedService<ISynchronousEncryptionKeyProvider>(
            PersistenceEncryptionKeys.EncryptionKeyProviderKey);
        var service1 = provider.GetRequiredKeyedService<ISynchronousSymmetricEncryptionService>(
            PersistenceEncryptionKeys.SymmetricEncryptionServiceKey);
        var service2 = provider.GetRequiredKeyedService<ISynchronousSymmetricEncryptionService>(
            PersistenceEncryptionKeys.SymmetricEncryptionServiceKey);

        keyProvider.Should().BeOfType<EncryptionOptionsKeyProvider>();
        service1.Should().BeOfType<SynchronousAesGcmEncryptionService>();
        service1.Should().BeSameAs(service2);

        var associatedData = "public.t.c"u8.ToArray();
        var payload = service1.Encrypt("round-trip"u8, associatedData);
        payload.KeyId.Should().Be("v1");
        service1.Decrypt(payload, associatedData).Value.Should().Equal("round-trip"u8.ToArray());
    }

    [Fact]
    public void WithEncryption_DoesNotRequireAddSharedKernelCryptography()
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

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });

        provider.GetService<ISymmetricEncryptionService>().Should().BeNull(
            "no ambient encryption service is registered or needed");
        provider.GetRequiredKeyedService<ISynchronousSymmetricEncryptionService>(
            PersistenceEncryptionKeys.SymmetricEncryptionServiceKey).Should().NotBeNull();
    }

    // -------------------------------------------------------------------------
    // Keyed-DI structural isolation — unrelated ambient (unkeyed) key provider and encryption service
    // registrations must have ZERO effect on .WithEncryption()'s own resolved provider, in either mode.
    // -------------------------------------------------------------------------

    [Fact]
    public void ConfigBackedDefault_UnrelatedUnkeyedRegistrations_HaveZeroEffect_OnResolvedKeyedProvider()
    {
        var services = new ServiceCollection();

        // Simulates unrelated general-purpose crypto registered elsewhere in the SAME container,
        // BEFORE .WithEncryption() — including an ambient synchronous service over different keys.
        var unrelatedKeys = new FakeEncryptionKeyProvider("unrelated");
        services.AddSingleton<IEncryptionKeyProvider>(unrelatedKeys);
        services.AddSingleton<ISynchronousEncryptionKeyProvider>(unrelatedKeys);
        services.AddSingleton<ISynchronousSymmetricEncryptionService>(new SynchronousAesGcmEncryptionService(unrelatedKeys));

        services
            .AddSharedKernelEfCore<TestDbContext>(opts => opts.UseSqlite("DataSource=:memory:").ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
            .WithEncryption(enc =>
            {
                enc.Enabled = true;
                enc.CurrentVersion = "v1";
                enc.Keys["v1"] = ValidBase64Key();
            })
            .Build();

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredKeyedService<ISynchronousEncryptionKeyProvider>(PersistenceEncryptionKeys.EncryptionKeyProviderKey)
            .Should().BeOfType<EncryptionOptionsKeyProvider>();
        provider.GetRequiredKeyedService<ISynchronousSymmetricEncryptionService>(PersistenceEncryptionKeys.SymmetricEncryptionServiceKey)
            .Encrypt("x"u8, []).KeyId.Should().Be("v1", "the persistence pipeline encrypts with its own configured key");

        // The ambient unkeyed slots are left exactly as the consumer registered them.
        provider.GetRequiredService<IEncryptionKeyProvider>().Should().BeSameAs(unrelatedKeys);
        provider.GetRequiredService<ISynchronousSymmetricEncryptionService>().Encrypt("x"u8, []).KeyId.Should().Be("unrelated");
    }

    [Fact]
    public void ExternalProviderMode_UnrelatedUnkeyedProvider_HasZeroEffect_OnResolvedKeyedProvider()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IEncryptionKeyProvider>(new FakeEncryptionKeyProvider("unrelated"));

        // The consumer's OWN registration of the type WithExternalEncryptionKeyProvider resolves —
        // deliberately a DIFFERENT concrete type than the unrelated ambient provider.
        services.AddSingleton<FakeRemoteEncryptionKeyProvider>();

        services
            .AddSharedKernelEfCore<TestDbContext>(opts => opts.UseSqlite("DataSource=:memory:").ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
            .WithEncryption(enc =>
            {
                enc.Enabled = true;
                enc.CurrentVersion = "v1";
                enc.Keys["v1"] = ValidBase64Key();
            })
            .WithExternalEncryptionKeyProvider<FakeRemoteEncryptionKeyProvider>()
            .Build();

        using var provider = services.BuildServiceProvider();

        var resolvedKeyProvider = provider.GetRequiredKeyedService<ISynchronousEncryptionKeyProvider>(
            PersistenceEncryptionKeys.EncryptionKeyProviderKey);

        resolvedKeyProvider.Should().BeOfType<PreWarmedEncryptionKeyProvider>(
            "WithExternalEncryptionKeyProvider<TProvider>() must resolve under the SAME keyed slot, " +
            "superseding the config-backed default's registration (last-registered-wins)");

        ((PreWarmedEncryptionKeyProvider)resolvedKeyProvider).Inner.Should().BeOfType<FakeRemoteEncryptionKeyProvider>(
            "the external mode must wrap the explicitly type-argument-selected TProvider, never the " +
            "unrelated ambient registration");
    }
}
