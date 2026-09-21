using SharedKernel.Application.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedKernel.Cryptography.Signing;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Persistence.EfCore.Encryption.Extensions;
using SharedKernel.Persistence.EfCore.Extensions;
using SharedKernel.Persistence.EfCore.MultiTenancy;
using SharedKernel.Testing.Persistence;

namespace SharedKernel.Persistence.EfCore.Encryption.Tests.TestFixtures;

/// <summary>Builds a fully DI-composed <see cref="EncryptionTestDbContext"/> host so <c>.WithEncryption()</c>'s real wiring runs end to end — never a hand-constructed DbContext.</summary>
public static class EncryptionTestHost
{
    public static readonly byte[] KeyV1 = Enumerable.Repeat((byte)0x11, 32).ToArray();
    public static readonly byte[] KeyV2 = Enumerable.Repeat((byte)0x22, 32).ToArray();

    public static ServiceProvider Build(
        string connectionString,
        string currentKeyId = "v1",
        FakeAuditActorContext? actorContext = null,
        bool allowUnencryptedValues = false,
        Action<IServiceCollection>? configureServices = null) =>
        Build<EncryptionTestDbContext>(connectionString, currentKeyId, actorContext, allowUnencryptedValues, configureServices);

    /// <summary>
    /// The generic counterpart of <see cref="Build"/>, for a test that needs a DIFFERENT
    /// <see cref="TenantedDbContext"/>-derived context (only <c>EncryptionRotationCheckpointIntegrationTests</c>
    /// today, against <see cref="EncryptionTestDbContextV2"/>) — otherwise identical wiring.
    /// </summary>
    public static ServiceProvider Build<TContext>(
        string connectionString,
        string currentKeyId = "v1",
        FakeAuditActorContext? actorContext = null,
        bool allowUnencryptedValues = false,
        Action<IServiceCollection>? configureServices = null)
            where TContext : TenantedDbContext
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var keys = new[]
        {
            new CryptographicKey("v1", KeyV1),
            new CryptographicKey("v2", KeyV2),
        };
        var keyProvider = new StaticEncryptionKeyProvider(currentKeyId, keys);
        services.AddSingleton(keyProvider);
        services.AddSingleton<IEncryptionKeyProvider>(sp => sp.GetRequiredService<StaticEncryptionKeyProvider>());
        services.AddSingleton<ISynchronousEncryptionKeyProvider>(sp => sp.GetRequiredService<StaticEncryptionKeyProvider>());

        var actor = actorContext ?? new FakeAuditActorContext();
        services.AddSingleton(actor);
        services.AddSingleton<SharedKernel.Application.Context.IRequestContext>(actor);
        services.AddSingleton<SharedKernel.Application.Context.IRequestContext>(actor);

        configureServices?.Invoke(services);

        services.AddSharedKernelEfCore<TContext>(options => options
            .UsePostgreSQL(TestNpgsqlDataSources.Get(connectionString))
            // Test-harness-only: every test builds its own fresh EncryptionInterceptor instance (a
            // materialization interceptor, part of EF Core's internal model-service-provider cache
            // key by design), so a single test PROCESS running many test methods legitimately builds
            // many such internal providers — never a concern for a real host, which calls
            //.WithEncryption() exactly once for the application's lifetime.
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
            .WithMultiTenancy()
            .WithEncryption(o => o.AllowUnencryptedValues = allowUnencryptedValues)
            .Build();

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }
}
