using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace SharedKernel.Persistence.EfCore.Extensibility;

/// <summary>
/// Extension point letting a sibling package (e.g. <c>SharedKernel.Persistence.EfCore.Encryption</c>)
/// contribute an EF Core model-finalizing convention to <see cref="Context.SharedKernelDbContext"/>
/// without that class needing a compile-time reference to the contributing package.
/// </summary>
/// <remarks>
/// <para>
/// Implementations are resolved from DI as <c>IEnumerable{IPersistenceModelConventionFactory}</c>
/// and invoked once, inside <see cref="Context.SharedKernelDbContext"/>'s constructor — the same
/// timing the pre-split code used to resolve its own encryption-specific keyed service. A factory
/// that has nothing to contribute for the current registration (e.g. its owning capability was never
/// opted into) is simply never registered — there is no "opted out" signal on this interface itself.
/// </para>
/// </remarks>
public interface IPersistenceModelConventionFactory
{
    /// <summary>
    /// Creates the convention to add to the model being built for <paramref name="context"/>.
    /// </summary>
    /// <param name="context">The <see cref="DbContext"/> instance being constructed.</param>
    /// <param name="options">The <see cref="DbContextOptions"/> supplied to that instance.</param>
    /// <returns>The convention to register.</returns>
    IConvention CreateConvention(DbContext context, DbContextOptions options);
}
