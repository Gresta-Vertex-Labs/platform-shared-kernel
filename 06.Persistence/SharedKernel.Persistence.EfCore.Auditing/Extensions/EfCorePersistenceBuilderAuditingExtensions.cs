using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using SharedKernel.Configuration.Extensions;
using SharedKernel.Persistence.Abstractions.Auditing;
using SharedKernel.Persistence.EfCore.Auditing.Chain;
using SharedKernel.Persistence.EfCore.Auditing.Checkpoints;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Extensibility;
using SharedKernel.Persistence.EfCore.Extensions;

namespace SharedKernel.Persistence.EfCore.Auditing.Extensions;

/// <summary>
/// Append-only, hash-chained audit-trail extension methods for <see cref="EfCorePersistenceBuilder{TContext}"/>.
/// </summary>
public static class EfCorePersistenceBuilderAuditingExtensions
{
    /// <summary>
    /// Opts in to the append-only, hash-chained audit-trail capability
    /// (<see cref="IAuditTrailWriter"/>/<see cref="IAuditQueryService"/>).
    /// </summary>
    /// <param name="builder">The persistence builder.</param>
    /// <param name="configuration">
    /// The root configuration <see cref="AuditChainOptions"/> binds against (section
    /// <see cref="AuditChainOptions.SectionName"/>).
    /// </param>
    /// <returns>The same builder for further chaining.</returns>
    /// <remarks>
    /// <para>
    /// Registers:
    /// <list type="bullet">
    /// <item><description><see cref="AuditChainOptions"/>, validated at startup (<c>HmacKeyBase64</c> is required, must decode to ≥32 bytes).</description></item>
    /// <item><description>An <see cref="IPersistenceModelConfigurator"/> that applies <see cref="AuditRecordEntityConfiguration"/> to the model.</description></item>
    /// <item><description><see cref="AuditRecordImmutabilityInterceptor"/> (tracked-entity guard) via <c>AddInterceptor&lt;TInterceptor&gt;</c>.</description></item>
    /// <item><description><see cref="AuditRecordMutationGuardInterceptor"/> (ExecuteUpdate/ExecuteDelete/raw-SQL guard) via an <see cref="IPersistenceOptionsExtension"/>.</description></item>
    /// <item><description><see cref="IAuditChainKeyProvider"/> → <see cref="ConfiguredAuditChainKeyProvider"/> (<c>TryAddSingleton</c> — register your own first to override).</description></item>
    /// <item><description><see cref="IAuditTrailWriter"/> → <see cref="EfAuditTrailWriter"/> (scoped).</description></item>
    /// <item><description><see cref="IAuditQueryService"/> → <see cref="EfAuditQueryService"/> (scoped).</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// <strong>REQUIRES</strong> <c>01.Core/SharedKernel.Cryptography</c>'s <c>AddSharedKernelCryptography()</c>
    /// (for <c>IHmacSigner</c>) to have been called. <strong>REQUIRES</strong>
    /// <c>SharedKernel.Persistence.Npgsql</c>'s <c>AddSharedKernelNpgsql()</c> to have been called — unlike the
    /// rest of this package, <see cref="EfAuditTrailWriter"/> takes a MANDATORY <c>IDbConnectionFactory</c>
    /// dependency (only <c>AddSharedKernelNpgsql()</c> registers one), so omitting it fails DI resolution
    /// outright rather than degrading gracefully. The one genuinely optional part of that registration is
    /// <c>IAdvisoryTransactionLock</c> for real per-chain append serialization — without a registered one,
    /// concurrent appends to the same chain fall back to unique-constraint retry alone (still correct, more
    /// retries under contention; see <see cref="EfAuditTrailWriter"/>'s own remarks). <strong>REQUIRES</strong>
    /// <c>EfCorePersistenceBuilder{TContext}.WithTransactionalUnitOfWork()</c> to have been called — also a
    /// MANDATORY <see cref="EfAuditTrailWriter"/> constructor dependency (<c>IAmbientDbTransaction</c>), which
    /// only that call registers. <strong>REQUIRES</strong> the PostgreSQL migration helper's
    /// <c>CreateImmutabilityTrigger</c> applied to the audit table in production — see
    /// <see cref="AuditRecordMutationGuardInterceptor"/>'s remarks for why the application-level guards alone
    /// are not sufficient.
    /// </para>
    /// <para>Optional. Omitting this call leaves all existing behavior unchanged — no <c>AuditRecord</c> table, no audit services registered.</para>
    /// </remarks>
    public static EfCorePersistenceBuilder<TContext> WithAuditTrail<TContext>(
        this EfCorePersistenceBuilder<TContext> builder,
        IConfiguration configuration)
        where TContext : SharedKernelDbContext
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configuration);

        if (builder.Services.Any(sd => sd.ServiceType == typeof(IAuditTrailWriter)))
            return builder;

        builder.Services.AddValidatedOptions<AuditChainOptions, AuditChainOptionsValidator>(
            configuration, validateDataAnnotations: true);

        builder.Services.AddSingleton<IPersistenceModelConfigurator, AuditRecordModelConfigurator>();
        builder.Services.AddSingleton<IPersistenceOptionsExtension, AuditRecordMutationGuardOptionsContributor>();
        builder.AddInterceptor<AuditRecordImmutabilityInterceptor>();

        builder.Services.TryAddSingleton<IAuditChainKeyProvider, ConfiguredAuditChainKeyProvider>();

        builder.Services.AddScoped<IAuditTrailWriter, EfAuditTrailWriter>();
        builder.Services.AddScoped<IAuditQueryService, EfAuditQueryService>();

        return builder;
    }

    /// <summary>
    /// Opts in to signed chain-head checkpoints (<see cref="IAuditCheckpointService"/>), on top of an
    /// already-called <see cref="WithAuditTrail{TContext}"/>.
    /// </summary>
    /// <param name="builder">The persistence builder.</param>
    /// <param name="signingKeyId">
    /// The <c>01.Core</c> <c>IAsymmetricSignatureService</c> key id checkpoints are signed with.
    /// </param>
    /// <returns>The same builder for further chaining.</returns>
    /// <remarks>
    /// <strong>REQUIRES</strong> <c>01.Core/SharedKernel.Cryptography</c>'s
    /// <c>AddSharedKernelCryptography().AddAsymmetricSigning()</c> to have been called — checkpoints
    /// are signed with a different key/algorithm family than the chain's own HMAC (see
    /// <see cref="Abstractions.Auditing.AuditChainCheckpoint"/>'s remarks). Optional; omitting this call
    /// leaves <see cref="Abstractions.Auditing.IAuditQueryService.VerifyChainFromCheckpointAsync"/>
    /// usable (a consumer can still hand it a checkpoint obtained some other way) but registers no
    /// <see cref="IAuditCheckpointService"/> to CREATE one.
    /// </remarks>
    public static EfCorePersistenceBuilder<TContext> WithAuditChainCheckpoints<TContext>(
        this EfCorePersistenceBuilder<TContext> builder,
        string signingKeyId)
        where TContext : SharedKernelDbContext
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(signingKeyId);

        builder.Services.PostConfigure<AuditChainOptions>(o => o.CheckpointSigningKeyId = signingKeyId);
        builder.Services.AddScoped<IAuditCheckpointService, EfAuditCheckpointService>();

        return builder;
    }
}
