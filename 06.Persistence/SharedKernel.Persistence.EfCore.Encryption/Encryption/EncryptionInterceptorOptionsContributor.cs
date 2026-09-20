using Microsoft.EntityFrameworkCore;
using SharedKernel.Persistence.EfCore.Extensibility;

namespace SharedKernel.Persistence.EfCore.Encryption;

/// <summary>
/// <see cref="IPersistenceOptionsExtension"/> that registers this container's <see cref="EncryptionInterceptor"/>
/// and <see cref="EncryptedColumnEqualityGuardInterceptor"/> singletons on every <see cref="DbContextOptionsBuilder"/>,
/// pooled or not.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately NOT wired through <c>EfCorePersistenceBuilder.AddInterceptor&lt;T&gt;()</c>, which registers its
/// interceptor type SCOPED — correct for a plain <c>ISaveChangesInterceptor</c>, wrong for
/// <see cref="EncryptionInterceptor"/>, which also implements <c>IMaterializationInterceptor</c> and therefore MUST
/// be the exact same object instance on every <see cref="DbContext"/> construction (EF Core keys its internal
/// service-provider cache on materialization-interceptor identity; a fresh instance per scope would rebuild that
/// cache — and therefore the compiled model — on every request). This contributor is constructed once, as a
/// singleton, by <c>EfCorePersistenceBuilder.WithEncryption()</c>, and calls
/// <see cref="DbContextOptionsBuilder.AddInterceptors(Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[])"/>
/// with that SAME instance on every call to <see cref="Apply"/> — always the identical interceptor object,
/// pooled or not.
/// </para>
/// <para>
/// <strong>Pooling:</strong> <see cref="Context.SharedKernelDbContext.OnConfiguring"/> (where
/// <see cref="IPersistenceOptionsExtension"/> contributions normally apply) never runs for a pooled context —
/// <c>Options.IsFrozen</c> is <see langword="true"/> from the very first construction. Rather than special-casing
/// encryption, <c>EfCorePersistenceBuilder{TContext}.Build()</c>'s pooled branch applies every registered
/// <see cref="IPersistenceOptionsExtension"/> itself, from the same pool-bound provider its platform interceptors
/// already resolve from — see that method's remarks. This contributor's own constructor-injected
/// <see cref="EncryptionInterceptor"/>/<see cref="EncryptedColumnEqualityGuardInterceptor"/> are Singleton, so
/// resolving them from that captive pool-bound provider is safe (identical to how
/// <c>TenantWriteGuardInterceptor</c> is resolved there for multi-tenancy).
/// </para>
/// </remarks>
public sealed class EncryptionInterceptorOptionsContributor : IPersistenceOptionsExtension
{
    private readonly EncryptionInterceptor _encryptionInterceptor;
    private readonly EncryptedColumnEqualityGuardInterceptor _equalityGuardInterceptor;

    /// <summary>Initialises a new <see cref="EncryptionInterceptorOptionsContributor"/>.</summary>
    public EncryptionInterceptorOptionsContributor(
        EncryptionInterceptor encryptionInterceptor,
        EncryptedColumnEqualityGuardInterceptor equalityGuardInterceptor)
    {
        ArgumentNullException.ThrowIfNull(encryptionInterceptor);
        ArgumentNullException.ThrowIfNull(equalityGuardInterceptor);
        _encryptionInterceptor = encryptionInterceptor;
        _equalityGuardInterceptor = equalityGuardInterceptor;
    }

    /// <inheritdoc />
    public void Apply(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.AddInterceptors(_encryptionInterceptor, _equalityGuardInterceptor);
}
