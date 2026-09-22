using Microsoft.EntityFrameworkCore;
using SharedKernel.Persistence.EfCore.Extensibility;

namespace SharedKernel.Persistence.EfCore.Encryption.Interception;

/// <summary>
/// Adds the singleton <see cref="EncryptionInterceptor"/> and <see cref="EncryptedMemberQueryGuard"/> to every
/// context's options, pooled or not.
/// </summary>
/// <remarks>
/// The same instances on every context: EF Core caches its internal service provider by the identity of
/// materialization interceptors, so a new instance per context would rebuild that cache every time. The core
/// package applies options extensions after its own interceptors and the service's, so encryption runs last on
/// save and sees every value set before it.
/// </remarks>
internal sealed class EncryptionOptionsContributor(EncryptionInterceptor encryption, EncryptedMemberQueryGuard queryGuard)
    : IPersistenceOptionsExtension
{
    public void Apply(DbContextOptionsBuilder optionsBuilder) => optionsBuilder.AddInterceptors(encryption, queryGuard);
}
