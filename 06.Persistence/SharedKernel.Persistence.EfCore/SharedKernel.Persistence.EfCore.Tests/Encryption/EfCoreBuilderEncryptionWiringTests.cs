using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
}
