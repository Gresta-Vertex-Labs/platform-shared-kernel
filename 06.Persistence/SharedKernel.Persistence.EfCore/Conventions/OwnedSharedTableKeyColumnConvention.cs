using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace SharedKernel.Persistence.EfCore.Conventions;

/// <summary>
/// Maps the key of an owned type stored in its owner's table (<c>OwnsOne</c>, table splitting) to the owner's key
/// column, as EF Core itself does, after snake_case naming.
/// </summary>
/// <remarks>
/// <c>EFCore.NamingConventions</c> prefixes every column of an owned type with the navigation name, including
/// the key it shares with its owner (<c>shipping_address_id</c> instead of <c>id</c>). When the owned key is a
/// configured property (the usual pattern when the owner's key is a value-converted strongly-typed id), the
/// owner and the owned type then map one primary key to two different columns and model validation fails. This
/// convention restores EF Core's rule: the primary key of a shared-table owned type uses the owner's key
/// columns, position by position. A column name configured explicitly (<c>HasColumnName</c>) is left alone.
/// </remarks>
internal sealed class OwnedSharedTableKeyColumnConvention : IModelFinalizingConvention
{
    /// <inheritdoc />
    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
        {
            var ownership = entityType.FindOwnership();
            if (ownership is null || !ownership.IsUnique)
                continue;

            var tableName = entityType.GetTableName();
            var owner = ownership.PrincipalEntityType;
            if (tableName is null
                || tableName != owner.GetTableName()
                || entityType.GetSchema() != owner.GetSchema())
            {
                continue;
            }

            // Table splitting requires both primary keys to map to the same columns, position by position.
            var primaryKey = entityType.FindPrimaryKey();
            var ownerKey = owner.FindPrimaryKey();
            if (primaryKey is null || ownerKey is null || primaryKey.Properties.Count != ownerKey.Properties.Count)
                continue;

            for (var i = 0; i < primaryKey.Properties.Count; i++)
            {
                var property = primaryKey.Properties[i];
                var source = property.GetColumnNameConfigurationSource();
                if (source is ConfigurationSource.Explicit or ConfigurationSource.DataAnnotation)
                    continue;

                var ownerColumn = ownerKey.Properties[i].GetColumnName();
                if (ownerColumn is not null && ownerColumn != property.GetColumnName())
                    property.SetColumnName(ownerColumn);
            }
        }
    }
}
