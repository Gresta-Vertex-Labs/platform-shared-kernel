using Microsoft.EntityFrameworkCore;

namespace SharedKernel.Persistence.EfCore.Extensibility;

/// <summary>
/// Extension point letting a sibling package contribute a <see cref="DbContextOptionsBuilder"/>
/// mutation from <see cref="Context.SharedKernelDbContext.OnConfiguring"/> without that class
/// needing a compile-time reference to the contributing package.
/// </summary>
/// <remarks>
/// <para>
/// Implementations are resolved from DI as <c>IEnumerable{IPersistenceOptionsExtension}</c>
/// and applied, in registration order, immediately after the platform interceptors are added — only
/// for a non-pooled context (the same <c>!optionsBuilder.Options.IsFrozen</c> guard that already
/// protects the interceptor-wiring call). <c>SharedKernel.Persistence.EfCore.Encryption</c> uses this
/// to register its stable-singleton <c>EncryptionInterceptor</c>/<c>EncryptedColumnEqualityGuardInterceptor</c>
/// pair (see <c>EncryptionInterceptorOptionsContributor</c>) without
/// <see cref="Context.SharedKernelDbContext"/> knowing encryption exists.
/// </para>
/// </remarks>
public interface IPersistenceOptionsExtension
{
    /// <summary>Applies this extension's mutation to <paramref name="optionsBuilder"/>.</summary>
    void Apply(DbContextOptionsBuilder optionsBuilder);
}
