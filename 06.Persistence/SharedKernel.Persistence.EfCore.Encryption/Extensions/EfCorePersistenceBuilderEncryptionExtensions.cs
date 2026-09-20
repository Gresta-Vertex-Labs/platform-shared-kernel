using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Configuration.Extensions;
using SharedKernel.Cryptography.Signing;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Encryption.BlindIndex;
using SharedKernel.Persistence.EfCore.Encryption.KeyRing;
using SharedKernel.Persistence.EfCore.Encryption.Rotation;
using SharedKernel.Persistence.EfCore.Extensibility;
using SharedKernel.Persistence.EfCore.Extensions;

namespace SharedKernel.Persistence.EfCore.Encryption.Extensions;

/// <summary>Field-level AES-256-GCM transparent-encryption extension methods for <see cref="EfCorePersistenceBuilder{TContext}"/>.</summary>
/// <remarks>
/// Every method here reaches into the builder exclusively through its public extensibility surface
/// (<see cref="EfCorePersistenceBuilder{TContext}.Services"/>, <see cref="EfCorePersistenceBuilder{TContext}.AddBuildAction"/>,
/// <see cref="EfCorePersistenceBuilder{TContext}.RequireDbContextFactory"/>,
/// <see cref="EfCorePersistenceBuilder{TContext}.IsDbContextPoolingEnabled"/>) — the same surface any other opt-in
/// capability package uses. It never calls <c>AddInterceptor&lt;T&gt;()</c> — see
/// <see cref="EncryptionInterceptorOptionsContributor"/>'s remarks for why.
/// </remarks>
#pragma warning disable RS0026 // Symbol has multiple public overloads with optional parameters.
// The defaults-only WithEncryption overload and the IConfiguration-bound overload differ in their
// second required parameter's presence/type (none vs. a mandatory IConfiguration) — a caller's own
// argument list already selects the correct overload; there is no shared call shape across the two
// for a trailing optional parameter to ever disambiguate incorrectly.
public static class EfCorePersistenceBuilderEncryptionExtensions
{
    /// <summary>
    /// Opts in to field-level AES-256-GCM transparent encryption, using defaults for <see cref="EncryptionOptions"/>.
    /// </summary>
    /// <param name="builder">The persistence builder.</param>
    /// <param name="configure">Optional action to configure <see cref="EncryptionOptions"/>.</param>
    /// <returns>The same builder for further chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown at <see cref="EfCorePersistenceBuilder{TContext}.Build"/> time when neither an
    /// <see cref="ISynchronousEncryptionKeyProvider"/> nor an <see cref="IEncryptionKeyProvider"/> is already
    /// registered, or when only <see cref="ISynchronousEncryptionKeyProvider"/> is (rotation and blind-index
    /// derivation need the asynchronous one too) — see the remarks below.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <strong>No key material is configured here.</strong> Register an <see cref="IEncryptionKeyProvider"/> (for
    /// rotation and, when no synchronous provider exists, decrypt/encrypt via a bridge —
    /// <see cref="EncryptionKeyRingCache"/>) and, ideally, also an <see cref="ISynchronousEncryptionKeyProvider"/>
    /// (for the runtime encrypt/decrypt path with no bridging overhead) BEFORE calling this method — e.g.
    /// <c>services.AddSingleton(new StaticEncryptionKeyProvider(...))</c> plus the two interface registrations its
    /// own XML doc example shows, or <c>13.ServiceDefaults</c>'s <c>AddSharedKernelKeyVaultKeyProvider()</c> for
    /// Azure Key Vault (asynchronous only — this method bridges it automatically).
    /// </para>
    /// <para>
    /// Combinable with <see cref="EfCorePersistenceBuilder{TContext}.WithDbContextPooling"/> — the encrypt/decrypt
    /// path reads tenant identity from the entity being saved/materialized (<c>IHasTenant.TenantId</c>), never from
    /// the pooled <see cref="SharedKernelDbContext"/> instance's own ambient state, so pool-slot reuse across
    /// different tenants/requests is safe by construction.
    /// </para>
    /// </remarks>
    public static EfCorePersistenceBuilder<TContext> WithEncryption<TContext>(
        this EfCorePersistenceBuilder<TContext> builder,
        Action<EncryptionOptions>? configure = null)
        where TContext : SharedKernelDbContext
    {
        ArgumentNullException.ThrowIfNull(builder);

        EnsureEncryptionInfrastructureRegistered(builder);

        if (configure is not null)
            builder.Services.AddOptions<EncryptionOptions>().Configure(configure);

        return builder;
    }

    /// <summary>
    /// Opts in to field-level AES-256-GCM transparent encryption, binding <see cref="EncryptionOptions"/> from
    /// <paramref name="configuration"/> via <see cref="EncryptionOptions.SectionName"/>.
    /// </summary>
    /// <param name="builder">The persistence builder.</param>
    /// <param name="configuration">The application's <see cref="IConfiguration"/>.</param>
    /// <param name="configure">Optional additional code-based configuration, layered on top of the bound values.</param>
    /// <returns>The same builder for further chaining.</returns>
    /// <remarks>See the parameterless overload's remarks — key material is never configured here either.</remarks>
    [RequiresUnreferencedCode("Binds configuration by reflection.")]
    [RequiresDynamicCode("Binds configuration by reflection.")]
    public static EfCorePersistenceBuilder<TContext> WithEncryption<TContext>(
        this EfCorePersistenceBuilder<TContext> builder,
        IConfiguration configuration,
        Action<EncryptionOptions>? configure = null)
        where TContext : SharedKernelDbContext
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configuration);

        EnsureEncryptionInfrastructureRegistered(builder);

        builder.Services.AddValidatedOptions<EncryptionOptions>(configuration);

        if (configure is not null)
            builder.Services.AddOptions<EncryptionOptions>().Configure(configure);

        return builder;
    }

    // Marker service used to detect whether EnsureEncryptionInfrastructureRegistered already ran for this
    // builder — extension methods have no per-builder-instance state of their own, so the DI container itself is
    // the state holder (mirrors the platform's existing "TryAdd"/"Services.Any(...)" idempotency idiom).
    private sealed class EncryptionInfrastructureMarker;

    private static void EnsureEncryptionInfrastructureRegistered<TContext>(EfCorePersistenceBuilder<TContext> builder)
        where TContext : SharedKernelDbContext
    {
        if (builder.Services.Any(sd => sd.ServiceType == typeof(EncryptionInfrastructureMarker)))
            return;

        builder.Services.AddSingleton<EncryptionInfrastructureMarker>();

        builder.Services.AddOptions<EncryptionOptions>();

        builder.Services.TryAddSingleton<EncryptionInterceptor>();
        builder.Services.TryAddSingleton<EncryptedColumnEqualityGuardInterceptor>();
        builder.Services.AddSingleton<IPersistenceOptionsExtension, EncryptionInterceptorOptionsContributor>();
        builder.Services.AddSingleton<IPersistenceModelConventionFactory, EncryptionModelConventionFactory>();
        builder.Services.TryAddSingleton<IBlindIndexService, BlindIndexService>();

        builder.RequireDbContextFactory();
        // Scoped, not singleton: EncryptionRotationService<TContext> depends on the SCOPED
        // IDbContextFactory<TContext> that TenantAwareDbContextFactory<TContext> wraps (see
        // EfCorePersistenceExtensions.Build's remarks) — a singleton registration would be a captive-
        // dependency violation, caught by ServiceProviderOptions.ValidateOnBuild.
        builder.Services.TryAddScoped<IEncryptionRotationJob, EncryptionRotationService<TContext>>();

        // Combinable with.WithDbContextPooling() — EncryptionInterceptor/EncryptedColumnEqualityGuardInterceptor
        // are already process-lifetime singletons with no per-request/per-tenant constructor-captured state (they
        // read the entity's own IHasTenant.TenantId live, never the DbContext's ambient CurrentTenant), and
        // EfCorePersistenceExtensions.Build's pooled branch applies every registered IPersistenceOptionsExtension
        // from the same pool-bound provider the platform interceptors already use — see that method's remarks.
        builder.AddBuildAction(() => EnsureCryptographyServicesRegistered(builder.Services));
    }

    // Fails loudly if the consuming service never registered a key provider, and bridges an asynchronous-only
    // one to the synchronous contract the runtime encrypt/decrypt path needs. Runs as a deferred build action so
    // it sees every registration the caller's builder chain made, regardless of call order relative to
    //.WithEncryption() itself.
    private static void EnsureCryptographyServicesRegistered(IServiceCollection services)
    {
        var hasSyncKeyProvider = services.Any(sd => sd.ServiceType == typeof(ISynchronousEncryptionKeyProvider));
        var hasAsyncKeyProvider = services.Any(sd => sd.ServiceType == typeof(IEncryptionKeyProvider));

        if (!hasSyncKeyProvider && !hasAsyncKeyProvider)
        {
            throw new InvalidOperationException(
                "'.WithEncryption()' requires an 'ISynchronousEncryptionKeyProvider' or an " +
                "'IEncryptionKeyProvider' already registered before it is called. " +
                "SharedKernel.Persistence.EfCore.Encryption never generates or stores key material itself — " +
                "register a 01.Core key provider, e.g. 'services.AddSingleton(new StaticEncryptionKeyProvider(" +
                "currentKeyId, keys))' (plus its 'IEncryptionKeyProvider'/'ISynchronousEncryptionKeyProvider' " +
                "registrations), or '13.ServiceDefaults'.'AddSharedKernelKeyVaultKeyProvider()' for Azure Key Vault.");
        }

        if (!hasAsyncKeyProvider)
        {
            throw new InvalidOperationException(
                "'.WithEncryption()' also requires an 'IEncryptionKeyProvider' (asynchronous) registered, even " +
                "when a genuine 'ISynchronousEncryptionKeyProvider' already handles the runtime encrypt/decrypt " +
                "path — 'IEncryptionRotationJob' and blind-index key derivation both need it. " +
                "'StaticEncryptionKeyProvider' already implements both interfaces; register it under both.");
        }

        if (!hasSyncKeyProvider)
        {
            // Bridge: the consumer registered only an asynchronous provider (e.g. a pure KMS provider with no
            // in-memory keys) — see EncryptionKeyRingCache's remarks for why this mirrors 01.Core's own
            // documented bridging pattern rather than being a workaround.
            services.TryAddSingleton(sp => new EncryptionKeyRingCache(
                sp.GetRequiredService<IEncryptionKeyProvider>(),
                sp.GetRequiredService<IOptions<EncryptionOptions>>().Value.KeyRingRetiredKeyIds));
            services.TryAddSingleton<ISynchronousEncryptionKeyProvider>(sp => sp.GetRequiredService<EncryptionKeyRingCache>());
            services.AddHostedService(sp => new EncryptionKeyRingRefreshHostedService(
                sp.GetRequiredService<EncryptionKeyRingCache>(),
                sp.GetRequiredService<IOptions<EncryptionOptions>>().Value.KeyRingRefreshInterval,
                sp.GetService<TimeProvider>() ?? TimeProvider.System,
                sp.GetRequiredService<ILogger<EncryptionKeyRingRefreshHostedService>>()));
        }

        services.TryAddSingleton<ISynchronousSymmetricEncryptionService, SynchronousAesGcmEncryptionService>();
        services.TryAddSingleton<ISymmetricEncryptionService, AesGcmEncryptionService>();
        services.TryAddSingleton<IHmacSigner, HmacSha256Signer>();
    }
}
#pragma warning restore RS0026
