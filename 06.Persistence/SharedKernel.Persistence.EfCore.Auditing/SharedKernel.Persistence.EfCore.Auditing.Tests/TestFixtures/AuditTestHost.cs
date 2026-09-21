using SharedKernel.Application.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using SharedKernel.Cryptography.Signing;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.Abstractions.Coordination;
using SharedKernel.Persistence.EfCore.Auditing.Chain;
using SharedKernel.Persistence.EfCore.Auditing.Extensions;
using SharedKernel.Persistence.EfCore.Extensions;
using SharedKernel.Persistence.Npgsql.Extensions;
using SharedKernel.Persistence.Npgsql.Options;
using SharedKernel.Persistence.PostgreSQL.Extensions;
using SharedKernel.Testing.Persistence;

namespace SharedKernel.Persistence.EfCore.Auditing.Tests.TestFixtures;

/// <summary>Builds a fully DI-composed <see cref="AuditChainTestDbContext"/> host so <c>.WithAuditTrail()</c>'s real wiring — including the real <c>NpgsqlAdvisoryTransactionLock</c> — runs end to end.</summary>
internal static class AuditTestHost
{
    public static readonly byte[] HmacKey = Enumerable.Repeat((byte)0x42, 32).ToArray();

    public static ServiceProvider Build(
        string connectionString,
        FakeAuditActorContext? actorContext = null,
        bool withAdvisoryLock = true,
        Action<IServiceCollection>? configureServices = null,
        Action<EfCorePersistenceBuilder<AuditChainTestDbContext>>? configureBuilder = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddDebug());

        var actor = actorContext ?? new FakeAuditActorContext();
        services.AddSingleton(actor);
        services.AddSingleton<IRequestContext>(actor);
        // Registered under BOTH the interface and the concrete type (same singleton instance) — a
        // test needs the concrete CrossTenantScope.Enter() capability, never exposed on the
        // read-only ICrossTenantScope interface itself (see its own remarks).
        services.AddSingleton<CrossTenantScope>();
        services.AddSingleton<ICrossTenantScope>(sp => sp.GetRequiredService<CrossTenantScope>());

        services.AddSingleton<IHmacSigner, HmacSha256Signer>();

        var configurationValues = new Dictionary<string, string?>
        {
            [$"{AuditChainOptions.SectionName}:{nameof(AuditChainOptions.HmacKeyBase64)}"] = Convert.ToBase64String(HmacKey),
            [$"{NpgsqlPersistenceOptions.SectionName}:{nameof(NpgsqlPersistenceOptions.ConnectionString)}"] = connectionString,
            // Testcontainers' Postgres image has no TLS configured — this is the documented,
            // explicit, auditable opt-down NpgsqlPersistenceOptionsValidator requires (never a
            // silent default), matching real local-dev/CI usage against a non-TLS test database.
            [$"{NpgsqlPersistenceOptions.SectionName}:{nameof(NpgsqlPersistenceOptions.SslMode)}"] = "Disable",
            [$"{NpgsqlPersistenceOptions.SectionName}:{nameof(NpgsqlPersistenceOptions.AcknowledgeInsecureSslMode)}"] = "true",
        };

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(configurationValues).Build();

        // AddSharedKernelNpgsql is ALWAYS called — it is the only registration source for
        // IDbConnectionFactory, which EfAuditTrailWriter needs unconditionally (the failure-path/
        // no-ambient-transaction connection). withAdvisoryLock controls ONLY whether
        // IAdvisoryTransactionLock (a SEPARATE registration from the same call) stays registered —
        // removed below to genuinely exercise the "no lock; retry-on-conflict only" fallback path.
        services.AddSharedKernelNpgsql(configuration);
        if (!withAdvisoryLock)
            services.RemoveAll<IAdvisoryTransactionLock>();

        configureServices?.Invoke(services);

        var builder = services.AddSharedKernelEfCore<AuditChainTestDbContext>(options => options
            .UsePostgreSQL(connectionString)
                // Test-harness-only: every test builds its own fresh DbContext model, so a single test
                // PROCESS running many test methods legitimately builds many internal EF service
                // providers — never a concern for a real host, which composes the container once.
                    .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning)))
                        .WithAuditTrail(configuration);


        configureBuilder?.Invoke(builder);

        builder.Build();

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }
}
