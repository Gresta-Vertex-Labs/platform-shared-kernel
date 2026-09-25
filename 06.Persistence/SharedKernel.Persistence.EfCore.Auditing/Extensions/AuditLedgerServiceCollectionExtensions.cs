using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Execution.Auditing;
using SharedKernel.Execution.Context;
using SharedKernel.Configuration.Extensions;
using SharedKernel.Cryptography.Signing;
using SharedKernel.Persistence.Abstractions.Connections;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.EfCore;
using SharedKernel.Persistence.EfCore.Auditing;
using SharedKernel.Persistence.EfCore.Auditing.Checkpoints;
using SharedKernel.Persistence.EfCore.Auditing.Maintenance;
using SharedKernel.Persistence.EfCore.Auditing.Querying;
using SharedKernel.Persistence.EfCore.Auditing.Sealing;
using SharedKernel.Persistence.EfCore.Auditing.SelfCheck;
using SharedKernel.Persistence.EfCore.Auditing.Writing;
using SharedKernel.Persistence.EfCore.Options;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Persistence;

/// <summary>Registers the audit ledger.</summary>
public static class AuditLedgerServiceCollectionExtensions
{
    /// <summary>
    /// Registers the audit ledger: the request-path writer (<see cref="IAuditTrailWriter"/> and
    /// its PostgreSQL implementation), <see cref="IAuditQueryService"/>, <see cref="IAuditCheckpointService"/>,
    /// <see cref="IAuditLedgerMaintenance"/>, <see cref="IAuditSealingProbe"/>, the background sealer, the
    /// startup self-check, and — unless registered earlier — the default <see cref="IAuditRecordAuthenticator"/>
    /// (the configured HMAC keyring) and <see cref="IAuditCheckpointSink"/>
    /// (the <c>audit_checkpoints</c> table). <see cref="AuditLedgerOptions"/> is validated at startup.
    /// Calling it again is a no-op.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The root configuration.</param>
    /// <returns>The same service collection.</returns>
    /// <remarks>
    /// Requires, from other registrations: <see cref="IDbConnectionFactory"/> (<c>AddSharedKernelNpgsql</c>),
    /// <c>IAmbientDbTransaction</c> and <see cref="ICrossTenantScope"/> (the EF Core persistence registration),
    /// <see cref="IRequestContext"/>, <see cref="IClock"/>, and <see cref="IHmacSigner"/>
    /// (<c>AddSharedKernelCryptography</c>). Checkpoints additionally need <see cref="IAsymmetricSignatureService"/>
    /// (<c>.AddAsymmetricSigning()</c>) and <see cref="AuditLedgerOptions.CheckpointSigningKeyId"/>. Create the
    /// tables with <c>migrationBuilder.CreateAuditLedgerTable()</c>.
    /// </remarks>
    public static IServiceCollection AddSharedKernelAuditLedger(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        if (services.Any(d => d.ServiceType == typeof(AuditLedgerRegistrationMarker)))
            return services;
        services.AddSingleton<AuditLedgerRegistrationMarker>();

        var customAuthenticator = services.Any(d => d.ServiceType == typeof(IAuditRecordAuthenticator));
        services.AddValidatedOptions<AuditLedgerOptions, AuditLedgerOptionsValidator>(configuration);
        if (customAuthenticator)
            services.PostConfigure<AuditLedgerOptions>(o => o.UsesCustomAuthenticator = true);

        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddSingleton<IAuditRecordAuthenticator, KeyringAuditRecordAuthenticator>();
        // Links and table checkpoints are written through the sealer's connection (its own role when
        // Sealer:DataSourceName is set); everything else uses the application's.
        services.TryAddSingleton<AuditSealerConnectionFactory>();
        services.TryAddSingleton<AuditSealedFloor>();
        services.TryAddSingleton<IAuditCheckpointSink>(sp => new TableAuditCheckpointSink(sp.GetRequiredService<AuditSealerConnectionFactory>()));
        services.TryAddSingleton<AuditSealingEngine>();
        services.TryAddSingleton(sp => new AuditCheckpointWriter(
            sp.GetRequiredService<IDbConnectionFactory>(),
            sp.GetRequiredService<IAuditRecordAuthenticator>(),
            sp.GetRequiredService<IAuditCheckpointSink>(),
            sp.GetRequiredService<IClock>(),
            sp.GetRequiredService<IOptions<AuditLedgerOptions>>(),
            sp.GetRequiredService<ILogger<AuditCheckpointWriter>>(),
            sp.GetService<IAsymmetricSignatureService>()));
        services.TryAddSingleton<IAuditSealingProbe, AuditSealingProbe>();
        services.TryAddSingleton<AuditLedgerSelfCheck>();

        services.TryAddScoped<EfAuditTrailWriter>();
        services.TryAddScoped<IAuditTrailWriter>(sp => sp.GetRequiredService<EfAuditTrailWriter>());
        services.TryAddScoped(sp => new LedgerSelfAudit(
            new AuditRecordFactory(
                new AuditCallerScope(sp.GetRequiredService<IRequestContext>(), sp.GetRequiredService<ICrossTenantScope>()),
                sp.GetRequiredService<IClock>(),
                sp.GetService<IOptions<PersistenceServiceOptions>>()?.Value.ServiceName ?? new PersistenceServiceOptions().ServiceName),
            sp.GetRequiredService<IDbConnectionFactory>()));
        services.TryAddScoped<IAuditQueryService, EfAuditQueryService>();
        services.TryAddScoped<IAuditCheckpointService, EfAuditCheckpointService>();
        services.TryAddScoped<IAuditLedgerMaintenance, AuditLedgerMaintenance>();

        services.AddHostedService<AuditLedgerSelfCheckHostedService>();
        services.AddHostedService<AuditSealerHostedService>();

        return services;
    }

    private sealed class AuditLedgerRegistrationMarker;
}
