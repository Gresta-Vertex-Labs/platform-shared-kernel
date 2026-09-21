using Microsoft.EntityFrameworkCore;
using SharedKernel.Persistence.EfCore.Extensibility;

namespace SharedKernel.Persistence.EfCore.MultiTenancy;

/// <summary>
/// Adds the row-level security interceptors to every <see cref="DbContextOptionsBuilder"/> of the
/// registration, pooled or not.
/// </summary>
internal sealed class RowLevelSecurityOptionsContributor(
    RowLevelSecurityCommandInterceptor commandInterceptor,
    RowLevelSecurityTransactionInterceptor transactionInterceptor,
    RowLevelSecuritySaveChangesInterceptor saveChangesInterceptor) : IPersistenceOptionsExtension
{
    public void Apply(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.AddInterceptors(commandInterceptor, transactionInterceptor, saveChangesInterceptor);
}
