using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;

namespace SharedKernel.Persistence.EfCore.Vectors;

/// <summary>
/// An EF Core <see cref="IModelFinalizingConvention"/> that registers the model-level
/// <c>CREATE EXTENSION IF NOT EXISTS vector</c> annotation, so a migration generated for this model
/// creates the pgvector extension automatically.
/// </summary>
/// <remarks>
/// Added to the convention set only when <c>UsePostgreSQL(..., useVector: true)</c> is
/// used — pgvector support is opt-in, so a service that never maps a vector column never gets an
/// unnecessary <c>CREATE EXTENSION</c> statement in its migrations.
/// </remarks>
internal sealed class VectorExtensionConvention : IModelFinalizingConvention
{
    /// <inheritdoc />
    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context)
    {
        modelBuilder.HasPostgresExtension("vector");
    }
}
