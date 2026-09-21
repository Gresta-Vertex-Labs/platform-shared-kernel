using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Persistence.EfCore.Concurrency;
using SharedKernel.Persistence.EfCore.Interceptors;

namespace SharedKernel.Persistence.EfCore.Conventions;

/// <summary>
/// Binds PostgreSQL's <c>xmin</c> system column as the optimistic-concurrency token of every aggregate root and
/// of every other entity implementing <see cref="IHasConcurrency"/>. Registered by <c>UsePostgres()</c>.
/// </summary>
/// <remarks>
/// <para>
/// An <see cref="IHasConcurrency"/> type keeps its <c>byte[] RowVersion</c> property, converted to the 4-byte
/// big-endian form of <c>xmin</c>. Every other aggregate root gets a shadow <see cref="uint"/> property named
/// <c>xmin</c> — no base class, no <c>RowVersion</c> column. Either way the database advances the value on every
/// write and EF Core adds it to the <c>WHERE</c> clause of every update and delete.
/// </para>
/// <para>
/// Runs first among the model-finalizing conventions (the plugin inserts it at the front), so EF Core's own
/// table-sharing convention, which runs later, adds the matching shadow token to owned types sharing the root's
/// table; and it does not depend on a derived context calling <c>base.OnModelCreating</c>.
/// </para>
/// <para>Skipped: owned and derived types (they use the root's row), keyless types and types not mapped to a table.</para>
/// </remarks>
internal sealed class XminConcurrencyTokenConvention : IModelFinalizingConvention
{
    private const string XidColumnType = "xid";

    /// <inheritdoc />
    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes().ToList())
        {
            if (entityType.IsOwned()
                || entityType.BaseType is not null
                || entityType.HasSharedClrType
                || entityType.FindPrimaryKey() is null
                || entityType.GetTableName() is null)
            {
                continue;
            }

            var clrType = entityType.ClrType;
            IConventionPropertyBuilder? token;

            if (typeof(IHasConcurrency).IsAssignableFrom(clrType))
            {
                token = entityType.FindProperty(nameof(IHasConcurrency.RowVersion))?.Builder;
                token?.HasConversion(new XminRowVersionValueConverter(), fromDataAnnotation: true);
            }
            else if (PersistenceSaveChangesInterceptor.IsAggregateRootClrType(clrType))
            {
                token = entityType.Builder.Property(typeof(uint), ConcurrencyVersion.XminColumn, fromDataAnnotation: true);
            }
            else
            {
                continue;
            }

            if (token is null)
                continue;

            token.IsConcurrencyToken(true, fromDataAnnotation: true);
            token.HasColumnName(ConcurrencyVersion.XminColumn, fromDataAnnotation: true);
            token.HasColumnType(XidColumnType, fromDataAnnotation: true);
            token.ValueGenerated(ValueGenerated.OnAddOrUpdate, fromDataAnnotation: true);
        }
    }
}
