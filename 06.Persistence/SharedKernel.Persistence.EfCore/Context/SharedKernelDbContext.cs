using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Persistence.EfCore.Auditing;
using SharedKernel.Persistence.EfCore.Encryption;
using SharedKernel.Persistence.EfCore.Interceptors;
using SharedKernel.Persistence.EfCore.Options;
using SharedKernel.Security.Abstractions.Abstractions;

namespace SharedKernel.Persistence.EfCore.Context;

/// <summary>
/// Abstract EF Core DbContext base for all SharedKernel-derived data contexts.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Interceptor registration:</strong> The constructor registers exactly three
/// <c>ISaveChangesInterceptor</c> instances — <see cref="AuditInterceptor"/>,
/// <see cref="SoftDeleteInterceptor"/>, and <see cref="ConcurrencyInterceptor"/> — via
/// <c>DbContextOptionsBuilder.AddInterceptors</c>. No <c>OutboxInterceptor</c> is registered
/// here; the outbox infrastructure is MassTransit's concern at the <c>07.Messaging</c> layer.
/// </para>
/// <para>
/// <strong>Model building:</strong> <see cref="OnModelCreating"/> calls
/// <see cref="ModelBuilder.ApplyConfigurationsFromAssembly"/> for the calling (concrete) context's
/// assembly, automatically discovering all <c>IEntityTypeConfiguration&lt;T&gt;</c> implementations.
/// It also registers <see cref="EncryptionModelConvention"/> to apply field-level encryption
/// to any property annotated with <c>.Encrypt()</c>.
/// Downstream contexts must call <c>base.OnModelCreating(modelBuilder)</c> first if they override
/// this method.
/// </para>
/// <para>
/// <strong>Save boundary:</strong> <see cref="SaveChangesAsync(CancellationToken)"/> is the
/// delegate used by <c>EfUnitOfWork</c>. Never call it directly from application or domain code —
/// always go through <c>IUnitOfWork.SaveChangesAsync</c>.
/// </para>
/// <para>
/// Concrete downstream contexts extend this base and add their <c>DbSet&lt;T&gt;</c> properties.
/// Multi-tenant contexts extend <see cref="SharedKernel.Persistence.EfCore.MultiTenancy.TenantedDbContext"/>
/// instead.
/// </para>
/// </remarks>
public abstract class SharedKernelDbContext : DbContext
{
    private readonly AuditInterceptor _auditInterceptor;
    private readonly SoftDeleteInterceptor _softDeleteInterceptor;
    private readonly ConcurrencyInterceptor _concurrencyInterceptor;
    private readonly IReadOnlyList<Microsoft.EntityFrameworkCore.Diagnostics.ISaveChangesInterceptor> _additionalInterceptors;
    private readonly IOptionsMonitor<EncryptionOptions> _encryptionOptions;
    private readonly IEncryptionVersionOverride _encryptionVersionOverride;
    private readonly ISymmetricEncryptionService? _symmetricEncryptionService;
    private readonly bool _auditTrailEnabled;

    /// <summary>
    /// Initialises a new <see cref="SharedKernelDbContext"/> and registers the three
    /// standard interceptors.
    /// </summary>
    /// <param name="options">EF Core context options supplied by the DI container.</param>
    /// <param name="auditInterceptor">Scoped interceptor that populates audit fields.</param>
    /// <param name="softDeleteInterceptor">Scoped interceptor that converts deletes to soft-deletes.</param>
    /// <param name="concurrencyInterceptor">Interceptor that wraps concurrency exceptions.</param>
    /// <param name="additionalInterceptors">
    /// Optional consumer-supplied interceptors. Platform three (Audit, SoftDelete, Concurrency) always
    /// fire before these — this ordering is intentional and cannot be overridden.
    /// </param>
    /// <param name="encryptionOptions">
    /// Optional live options monitor for field-level encryption. When <see langword="null"/> or when
    /// <c>WithEncryption()</c> has not been called, encryption defaults to disabled (pass-through).
    /// All existing <c>SharedKernelDbContext</c> subclass constructors remain compatible — this
    /// parameter is nullable optional and defaults to a no-op monitor.
    /// </param>
    /// <param name="encryptionVersionOverride">
    /// Optional scoped rotation-target-version accessor passed to <see cref="EncryptionModelConvention"/>.
    /// When <see langword="null"/> (e.g., <c>WithEncryption()</c> has not been called), a shared
    /// no-op instance is used and <see cref="EncryptedValueConverter"/> always encrypts with
    /// <see cref="EncryptionOptions.CurrentVersion"/>.
    /// </param>
    /// <param name="symmetricEncryptionService">
    /// Optional cryptographic service used by <see cref="EncryptedValueConverter"/> for AES-256-GCM
    /// operations (P-227). Resolved from DI when <c>AddSharedKernelCryptography()</c> and
    /// <c>.WithEncryption()</c> have been called. When <see langword="null"/>, the converter operates
    /// in disabled pass-through mode.
    /// </param>
    /// <param name="encryptionKeyProvider">
    /// Retained for constructor source-compatibility only (D-109/P-448) — as of P-448 this value is
    /// no longer forwarded to <see cref="EncryptionModelConvention"/>, which stopped needing an
    /// <see cref="IEncryptionKeyProvider"/> directly (D-108). Downstream contexts may keep declaring
    /// and forwarding this parameter without any behavior change; <c>01.Core</c>'s
    /// <c>AesGcmEncryptionService</c> still resolves <see cref="IEncryptionKeyProvider"/> from DI on
    /// its own.
    /// </param>
    /// <param name="auditTrailMarker">
    /// WO-071/P-457. Optional marker resolved from DI — present only when
    /// <c>EfCorePersistenceBuilder.WithAuditTrail()</c> registered <see cref="AuditTrailFeatureMarker"/>.
    /// When non-<see langword="null"/>, <see cref="OnModelCreating"/> applies
    /// <see cref="AuditRecordEntityConfiguration"/> so <c>AuditRecord</c> becomes part of this
    /// context's model. A downstream context that wants the audit trail must declare this parameter
    /// in its own constructor and forward it to <c>base(...)</c>, exactly like
    /// <paramref name="symmetricEncryptionService"/>/<paramref name="encryptionKeyProvider"/> already
    /// require for <c>.WithEncryption()</c>. Defaults to <see langword="null"/> — every existing
    /// downstream context is unaffected. See <see cref="AuditTrailFeatureMarker"/>'s remarks for why
    /// a marker type is used instead of a raw <see langword="bool"/> (DI cannot resolve a primitive
    /// constructor parameter automatically).
    /// </param>
    protected SharedKernelDbContext(
        DbContextOptions options,
        AuditInterceptor auditInterceptor,
        SoftDeleteInterceptor softDeleteInterceptor,
        ConcurrencyInterceptor concurrencyInterceptor,
        IEnumerable<Microsoft.EntityFrameworkCore.Diagnostics.ISaveChangesInterceptor>? additionalInterceptors = null,
        IOptionsMonitor<EncryptionOptions>? encryptionOptions = null,
        IEncryptionVersionOverride? encryptionVersionOverride = null,
        ISymmetricEncryptionService? symmetricEncryptionService = null,
        IEncryptionKeyProvider? encryptionKeyProvider = null,
        AuditTrailFeatureMarker? auditTrailMarker = null)
        : base(options)
    {
        _auditInterceptor = auditInterceptor;
        _softDeleteInterceptor = softDeleteInterceptor;
        _concurrencyInterceptor = concurrencyInterceptor;
        _additionalInterceptors = additionalInterceptors?.ToList() ?? [];
        _encryptionOptions = encryptionOptions ?? NullOptionsMonitor<EncryptionOptions>.Instance;
        _encryptionVersionOverride = encryptionVersionOverride ?? EncryptionVersionOverride.NoOp;
        // D-131/P-498/WO-081: this package's own persistence-scoped ISymmetricEncryptionService,
        // registered by EfCorePersistenceBuilder.WithEncryption() under a package-internal keyed-DI
        // slot (never the ambient unkeyed slot), ALWAYS wins over whatever this constructor
        // parameter happens to carry when it is resolvable — a downstream context that mirrors this
        // base constructor's full parameter list would otherwise have symmetricEncryptionService
        // silently auto-populated by DI from an unrelated general-purpose
        // AddSharedKernelCryptography() registration, reintroducing the exact registration-order
        // collision this phase closes. The explicit parameter remains the fallback for hand
        // construction (tests) and for the "no .WithEncryption() call at all" pass-through case,
        // where no ApplicationServiceProvider/keyed registration exists at all.
        _symmetricEncryptionService =
            ResolveKeyedSymmetricEncryptionService(options) ?? symmetricEncryptionService;
        // D-109/P-448: encryptionKeyProvider is intentionally not stored — it is retained on this
        // constructor's signature only for source-compatibility (see the parameter's XML docs above).
        _auditTrailEnabled = auditTrailMarker is not null;

        // WO-051/P-322: initialised from AuditInterceptor's own constructor-captured IUserContext —
        // deliberately NOT a new constructor parameter on this class (auditInterceptor is already
        // passed in above). Under the default, non-pooled registration this is the correct value for
        // this instance's entire lifetime. Under .WithDbContextPooling(), RefreshUserContext(...) is
        // called once per lease to replace it with the CURRENT scope's real IUserContext.
        CurrentUserContext = auditInterceptor.UserContext;
    }

    /// <summary>
    /// Gets the <see cref="IUserContext"/> that <see cref="AuditInterceptor"/> and
    /// <see cref="SoftDeleteInterceptor"/> resolve audit fields from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Under the default (non-pooled) registration this is set once at construction — from
    /// <see cref="AuditInterceptor.UserContext"/> — and never changes for this instance's lifetime,
    /// which is already correct because a fresh <see cref="SharedKernelDbContext"/> instance is
    /// constructed per DI scope.
    /// </para>
    /// <para>
    /// <strong>Pooling (WO-051/P-322):</strong> under
    /// <c>EfCorePersistenceBuilder.WithDbContextPooling()</c>, a pooled instance's constructor runs
    /// ONCE per pooled slot, not once per lease. <see cref="AuditInterceptor"/>/
    /// <see cref="SoftDeleteInterceptor"/> read this property LIVE off
    /// <c>eventData.Context</c> inside <c>SavingChanges</c>/<c>SavingChangesAsync</c> — always the
    /// CURRENT executing instance — instead of their own constructor-captured field, so calling
    /// <see cref="RefreshUserContext"/> once per lease keeps audit attribution correct across
    /// unrelated requests reusing the same pooled instance.
    /// </para>
    /// </remarks>
    public IUserContext CurrentUserContext { get; private set; }

    /// <summary>
    /// Replaces <see cref="CurrentUserContext"/> with <paramref name="userContext"/>.
    /// </summary>
    /// <param name="userContext">The current scope's real <see cref="IUserContext"/>.</param>
    /// <remarks>
    /// Called once per lease by the factory delegate <c>EfCorePersistenceBuilder.WithDbContextPooling()</c>
    /// registers. Non-pooled consumers never need to call this — the constructor-set value is already
    /// correct for a non-pooled instance's lifetime.
    /// </remarks>
    public void RefreshUserContext(IUserContext userContext) => CurrentUserContext = userContext;

    /// <summary>
    /// Gets the scoped <see cref="IEncryptionVersionOverride"/> instance injected into this context,
    /// or the shared no-op instance when <c>WithEncryption()</c> has not been called.
    /// </summary>
    /// <remarks>
    /// Exposed so that <see cref="EncryptionRotationService{TContext}"/> can direct this context's
    /// <see cref="EncryptedValueConverter"/> instances to a target key version during a rotation
    /// batch — the same instance is resolved by <see cref="EncryptionModelConvention"/> via
    /// <see cref="ConfigureConventions"/>, so setting <see cref="IEncryptionVersionOverride.OverrideVersion"/>
    /// here affects this context's converters without any additional DI resolution.
    /// </remarks>
    /// <seealso cref="IEncryptionVersionOverride"/>
    internal IEncryptionVersionOverride CurrentEncryptionVersionOverride => _encryptionVersionOverride;

    /// <summary>Gets the clock this context's audit interceptor uses, attached to every aggregate it materializes.</summary>
    /// <seealso cref="DomainClockMaterializationInterceptor"/>
    internal SharedKernel.Primitives.Clocks.IClock Clock => _auditInterceptor.Clock;

    /// <inheritdoc />
    /// <remarks>
    /// <strong>Pooling guard (WO-051/P-322):</strong> when <c>optionsBuilder.Options.IsFrozen</c> is
    /// <see langword="true"/> — which EF Core sets for every instance constructed via
    /// <c>EfCorePersistenceBuilder.WithDbContextPooling()</c>'s
    /// <c>AddPooledDbContextFactory&lt;TContext&gt;</c> registration, confirmed empirically to be
    /// <see langword="true"/> even on the very FIRST pool-miss construction — any attempt to mutate
    /// <paramref name="optionsBuilder"/> here throws <see cref="InvalidOperationException"/>
    /// ("'OnConfiguring' cannot be used to modify DbContextOptions when DbContext pooling is
    /// enabled.") the first time the context's internal services are built (e.g., on
    /// <c>SaveChangesAsync</c> or <c>EnsureCreatedAsync</c>), not immediately at the mutation call
    /// site itself. <see cref="EfCorePersistenceBuilder{TContext}.WithDbContextPooling"/>'s pooled
    /// registration therefore pre-adds the identical platform-three-plus-additional interceptor set
    /// (and the <see cref="IEncryptionVersionOverride"/> extension) directly into the pool's own
    /// <c>optionsAction</c> — BEFORE freezing — so this method correctly does nothing extra for a
    /// pooled context; for a non-pooled context (<c>Options.IsFrozen == false</c>), this method
    /// performs the wiring exactly as before.
    /// </remarks>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.Options.IsFrozen)
        {
            // Platform interceptors always fire first — consumer interceptors are appended after.
            var interceptors = new List<Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor>
            {
                _auditInterceptor,
                _softDeleteInterceptor,
                _concurrencyInterceptor,
                DomainClockMaterializationInterceptor.FromContext,
            };
            interceptors.AddRange(_additionalInterceptors);

            optionsBuilder.AddInterceptors(interceptors);

            // EF Core's default model cache is keyed by context type and is shared process-wide across
            // all DbContext instances of this type — including instances from different IServiceProvider
            // containers. Incorporate this context's IEncryptionVersionOverride instance into the cache
            // key so EncryptionModelConvention's converters are always bound to the override singleton
            // actually injected into THIS container. See EncryptionAwareModelCacheKeyFactory for the
            // full rationale.
            optionsBuilder.WithEncryptionVersionOverride(_encryptionVersionOverride);
        }

        base.OnConfiguring(optionsBuilder);
    }

    /// <summary>
    /// Applies all <c>IEntityTypeConfiguration&lt;T&gt;</c> implementations discovered in the
    /// concrete context's assembly, and registers the <see cref="EncryptionModelConvention"/>
    /// for field-level encryption.
    /// </summary>
    /// <param name="modelBuilder">The builder used to construct the model for this context.</param>
    /// <remarks>
    /// Downstream contexts that override this method must call
    /// <c>base.OnModelCreating(modelBuilder)</c> first to ensure configurations are applied.
    /// </remarks>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(GetType().Assembly);

        // WO-071/P-457: AuditRecordEntityConfiguration lives in THIS assembly
        // (SharedKernel.Persistence.EfCore), not the downstream concrete context's assembly, so the
        // ApplyConfigurationsFromAssembly(GetType().Assembly) scan above never discovers it — applied
        // explicitly here, and only when WithAuditTrail() opted in.
        if (_auditTrailEnabled)
            modelBuilder.ApplyConfiguration(new AuditRecordEntityConfiguration());

        base.OnModelCreating(modelBuilder);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <strong>Concurrency-conflict translation (CORRECTED, WO-051/P-315):</strong> wraps the base
    /// save call so <see cref="ConcurrencyInterceptor.TryTranslate"/> can convert a
    /// <see cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException"/> affecting an
    /// <see cref="SharedKernel.Domain.Abstractions.IHasConcurrency"/> entity into a
    /// <see cref="SharedKernel.Core.Exceptions.ConflictException"/>. This is the actual enforcement
    /// point for that translation — EF Core 10 does not allow
    /// <c>ISaveChangesInterceptor.SaveChangesFailed</c>/<c>SaveChangesFailedAsync</c> to replace the
    /// exception propagating from <c>SaveChanges</c> (confirmed empirically; see
    /// <see cref="ConcurrencyInterceptor"/>'s class remarks for the full story). The exception
    /// filter (<c>when (... is { } conflict)</c>) means a non-matching exception is never caught
    /// here at all — it propagates with its original stack trace fully intact, identical to
    /// today's behavior for every exception this translation does not apply to.
    /// </remarks>
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        try
        {
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }
        catch (DbUpdateConcurrencyException ex) when (_concurrencyInterceptor.TryTranslate(ex) is { } conflict)
        {
            throw conflict;
        }
    }

    /// <inheritdoc />
    /// <remarks>See <see cref="SaveChanges(bool)"/> for the concurrency-conflict translation this override performs.</remarks>
    public override async Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex) when (_concurrencyInterceptor.TryTranslate(ex) is { } conflict)
        {
            throw conflict;
        }
    }

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // P-227: Pass ISymmetricEncryptionService to the convention so EncryptedValueConverter uses
        // the delegated crypto path. D-109/P-448: IEncryptionKeyProvider is no longer forwarded here
        // — EncryptionModelConvention/EncryptedValueConverter stopped needing it directly (D-108).
        configurationBuilder.Conventions.Add(
            _ => new EncryptionModelConvention(
                _encryptionOptions,
                _symmetricEncryptionService,
                _encryptionVersionOverride));

        base.ConfigureConventions(configurationBuilder);
    }

    // D-131/P-498/WO-081: resolves this package's own persistence-scoped ISymmetricEncryptionService
    // from the CURRENT DI scope's IServiceProvider — the same one AddDbContext<TContext> used to
    // construct this instance — via the keyed-DI slot EfCorePersistenceBuilder.WithEncryption()
    // registers (PersistenceEncryptionKeys.SymmetricEncryptionServiceKey). Never the ambient unkeyed
    // ISymmetricEncryptionService slot. CoreOptionsExtension.ApplicationServiceProvider is EF Core's
    // own public mechanism for a DbContext to reach the container that constructed it; it is null
    // for a hand-built DbContextOptions (e.g. every existing unit test in this package that
    // constructs a SharedKernelDbContext subclass directly), in which case this returns null and the
    // caller falls back to whatever was explicitly passed to the constructor.
    private static ISymmetricEncryptionService? ResolveKeyedSymmetricEncryptionService(DbContextOptions options)
    {
        var applicationServiceProvider = options.FindExtension<CoreOptionsExtension>()?.ApplicationServiceProvider;
        return applicationServiceProvider?.GetKeyedService<ISymmetricEncryptionService>(
            PersistenceEncryptionKeys.SymmetricEncryptionServiceKey);
    }
}
