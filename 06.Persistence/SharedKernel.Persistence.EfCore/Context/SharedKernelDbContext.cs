using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
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
    protected SharedKernelDbContext(
        DbContextOptions options,
        AuditInterceptor auditInterceptor,
        SoftDeleteInterceptor softDeleteInterceptor,
        ConcurrencyInterceptor concurrencyInterceptor,
        IEnumerable<Microsoft.EntityFrameworkCore.Diagnostics.ISaveChangesInterceptor>? additionalInterceptors = null,
        IOptionsMonitor<EncryptionOptions>? encryptionOptions = null)
        : base(options)
    {
        _auditInterceptor = auditInterceptor;
        _softDeleteInterceptor = softDeleteInterceptor;
        _concurrencyInterceptor = concurrencyInterceptor;
        _additionalInterceptors = additionalInterceptors?.ToList() ?? [];
        _encryptionOptions = encryptionOptions ?? NullOptionsMonitor<EncryptionOptions>.Instance;
    }

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
        configurationBuilder.Conventions.Add(
            _ => new EncryptionModelConvention(_encryptionOptions));

        base.ConfigureConventions(configurationBuilder);
    }
}
