using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using SharedKernel.Persistence.PostgreSQL.Vector;

namespace SharedKernel.Persistence.PostgreSQL.Conventions;

/// <summary>
/// EF Core <see cref="IConventionSetPlugin"/> that adds <see cref="SnakeCaseNamingConvention"/>,
/// <see cref="XminConcurrencyTokenConvention"/>, and (opt-in) <see cref="VectorExtensionConvention"/>
/// to every model built for a <c>DbContext</c> configured via <c>UsePostgreSQL()</c>.
/// </summary>
/// <remarks>
/// <see cref="IConventionSetPlugin"/> is EF Core's supported extension point for a provider or
/// add-on package to append model-building conventions without requiring the consuming
/// <c>DbContext</c> to override <c>ConfigureConventions</c> itself — the same mechanism used by
/// community naming-convention packages. Registered into EF Core's internal service provider by
/// <see cref="PostgreSQLConventionsOptionsExtension.ApplyServices"/>.
/// </remarks>
internal sealed class PostgreSQLConventionSetPlugin(bool useVector) : IConventionSetPlugin
{
    /// <inheritdoc />
    /// <remarks>
    /// <see cref="SnakeCaseNamingConvention"/> is added first so that
    /// <see cref="XminConcurrencyTokenConvention"/>'s explicit <c>"xmin"</c> column name
    /// (applied second) always wins over the snake-cased default that would otherwise apply to the
    /// <c>RowVersion</c> property.
    /// </remarks>
    public ConventionSet ModifyConventions(ConventionSet conventionSet)
    {
        conventionSet.ModelFinalizingConventions.Add(new SnakeCaseNamingConvention());
        conventionSet.ModelFinalizingConventions.Add(new XminConcurrencyTokenConvention());

        if (useVector)
            conventionSet.ModelFinalizingConventions.Add(new VectorExtensionConvention());

        return conventionSet;
    }
}
