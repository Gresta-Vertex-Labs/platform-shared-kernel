using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Persistence.EfCore.Encryption;
using SharedKernel.Persistence.EfCore.Interceptors;
using SharedKernel.Persistence.EfCore.Options;

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
    private readonly IEncryptionKeyProvider? _encryptionKeyProvider;

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
    /// Optional key provider bridging <see cref="EncryptionOptions"/> to
    /// <see cref="IEncryptionKeyProvider"/> (P-227). Registered as scoped by
    /// <c>EfCorePersistenceBuilder.WithEncryption()</c>. When <see langword="null"/>, the converter
    /// operates in disabled pass-through mode.
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
        IEncryptionKeyProvider? encryptionKeyProvider = null)
        : base(options)
    {
        _auditInterceptor = auditInterceptor;
        _softDeleteInterceptor = softDeleteInterceptor;
        _concurrencyInterceptor = concurrencyInterceptor;
        _additionalInterceptors = additionalInterceptors?.ToList() ?? [];
        _encryptionOptions = encryptionOptions ?? NullOptionsMonitor<EncryptionOptions>.Instance;
        _encryptionVersionOverride = encryptionVersionOverride ?? EncryptionVersionOverride.NoOp;
        _symmetricEncryptionService = symmetricEncryptionService;
        _encryptionKeyProvider = encryptionKeyProvider;
    }

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

    /// <inheritdoc />
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // Platform interceptors always fire first — consumer interceptors are appended after.
        var interceptors = new List<Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor>
        {
            _auditInterceptor,
            _softDeleteInterceptor,
            _concurrencyInterceptor
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
        base.OnModelCreating(modelBuilder);
    }

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // P-227: Pass ISymmetricEncryptionService and IEncryptionKeyProvider to the convention
        // so EncryptedValueConverter uses the delegated crypto path.
        configurationBuilder.Conventions.Add(
            _ => new EncryptionModelConvention(
                _encryptionOptions,
                _symmetricEncryptionService,
                _encryptionKeyProvider,
                _encryptionVersionOverride));

        base.ConfigureConventions(configurationBuilder);
    }
}
