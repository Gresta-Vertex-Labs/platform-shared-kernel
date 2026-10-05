using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using SharedKernel.Persistence.EfCore.Extensibility;

namespace SharedKernel.Persistence.EfCore.Encryption.Metadata;

/// <summary>Contributes <see cref="EncryptionModelConvention"/> to every <c>SharedKernelDbContext</c> model.</summary>
internal sealed class EncryptionModelConventionFactory : IPersistenceModelConventionFactory
{
    private static readonly EncryptionModelConvention Convention = new();

    public IConvention CreateConvention(DbContext context, DbContextOptions options) => Convention;
}
