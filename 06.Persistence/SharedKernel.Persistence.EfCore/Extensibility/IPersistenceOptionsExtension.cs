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
/// and applied, in registration order, immediately after the platform interceptors are added —
/// on BOTH the pooled and non-pooled paths. <see cref="Context.SharedKernelDbContext.OnConfiguring"/>
/// calls <c>PersistenceContextDependencies.ApplyTo</c> (which applies every registered instance of
/// this interface) only when <c>!optionsBuilder.Options.IsFrozen</c> — true for a non-pooled context,
/// never for a pooled one, since a pooled context's options are frozen before
/// <see cref="Context.SharedKernelDbContext.OnConfiguring"/> ever runs.
/// the pooled-factory callback of <c>AddSharedKernelPostgres</c> calls the SAME
/// <c>PersistenceContextDependencies.ApplyTo</c> method directly instead, against the pool's own
/// <c>optionsAction</c>, before freezing — so both paths funnel through the identical application
/// logic and cannot silently drift apart. <c>SharedKernel.Persistence.EfCore.Encryption</c> uses this
/// interface to register its stable-singleton <c>EncryptionInterceptor</c>/<c>EncryptedColumnEqualityGuardInterceptor</c>
/// pair (see <c>EncryptionInterceptorOptionsContributor</c>) without
/// <see cref="Context.SharedKernelDbContext"/> knowing encryption exists.
/// </para>
/// </remarks>
public interface IPersistenceOptionsExtension
{
    /// <summary>Applies this extension's mutation to <paramref name="optionsBuilder"/>.</summary>
    void Apply(DbContextOptionsBuilder optionsBuilder);
}
