using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using SharedKernel.Persistence.EfCore.Vectors;

namespace SharedKernel.Persistence.EfCore.Conventions;

/// <summary>
/// EF Core <see cref="IConventionSetPlugin"/> that adds <see cref="OwnedSharedTableKeyColumnConvention"/>,
/// <see cref="PostgresIdentifierLengthConvention"/> and (opt-in) <see cref="VectorExtensionConvention"/> to every
/// model built for a <c>DbContext</c> configured via <c>UsePostgres()</c>.
/// </summary>
/// <remarks>
/// snake_case naming itself comes from <c>EFCore.NamingConventions</c> (<c>UseSnakeCaseNamingConvention()</c>),
/// which renames names as they are configured; the identifier-length convention runs at model finalization,
/// after every rename, so it sees the final names.
/// </remarks>
internal sealed class PostgresConventionSetPlugin(bool useVector) : IConventionSetPlugin
{
    /// <inheritdoc />
    public ConventionSet ModifyConventions(ConventionSet conventionSet)
    {
        // First: before EF Core's table-sharing convention, which copies the token to owned types sharing the table.
        conventionSet.ModelFinalizingConventions.Insert(0, new XminConcurrencyTokenConvention());

        if (useVector)
            conventionSet.ModelFinalizingConventions.Add(new VectorExtensionConvention());

        conventionSet.ModelFinalizingConventions.Add(new OwnedSharedTableKeyColumnConvention());
        conventionSet.ModelFinalizingConventions.Add(new PostgresIdentifierLengthConvention());

        return conventionSet;
    }
}
