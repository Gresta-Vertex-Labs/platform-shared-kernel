using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using SharedKernel.Persistence.EfCore.Extensibility;

namespace SharedKernel.Persistence.EfCore.Encryption;

/// <summary>
/// <see cref="IPersistenceModelConventionFactory"/> that contributes a fresh <see cref="EncryptionModelConvention"/>
/// per <see cref="DbContext"/> instance.
/// </summary>
/// <remarks>
/// Registered as a singleton by <c>EfCorePersistenceBuilder.WithEncryption()</c>, resolved by
/// <c>SharedKernel.Persistence.EfCore</c>'s <c>SharedKernelDbContext</c> via
/// <c>IEnumerable&lt;IPersistenceModelConventionFactory&gt;</c> — <c>SharedKernelDbContext</c> has no compile-time
/// reference to this package at all. <see cref="EncryptionModelConvention"/> carries no state of its own (unlike
/// its predecessor, which closed over a crypto service to build value converters), so this factory has nothing to
/// inject either — it exists purely to satisfy the <see cref="IPersistenceModelConventionFactory"/> contract.
/// </remarks>
public sealed class EncryptionModelConventionFactory : IPersistenceModelConventionFactory
{
    /// <inheritdoc />
    public IConvention CreateConvention(DbContext context, DbContextOptions options) => new EncryptionModelConvention();
}
