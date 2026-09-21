using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Persistence.EfCore.Encryption.Metadata;

namespace SharedKernel.Persistence.EfCore.Encryption.Maintenance;

/// <summary>One physical encrypted column the maintenance job processes.</summary>
/// <param name="Key">The column's identity, <c>schema.table.column</c>; the checkpoint refers to it by this name.</param>
/// <param name="Member">The encrypted property mapped to the column (the first one, for a TPH column shared by siblings).</param>
/// <param name="Table">The delimited table.</param>
/// <param name="QualifiedTableName">The table as a <c>regclass</c> literal, for statistics.</param>
/// <param name="PrimaryKeyColumn">The delimited primary-key column of <paramref name="Table"/>.</param>
/// <param name="KeyKind">The primary key's provider type.</param>
/// <param name="ValueColumn">The delimited encrypted column.</param>
/// <param name="BlindIndexColumn">The delimited blind-index column, when the property has one.</param>
/// <param name="TenantSource">
/// How to read the row's tenant: <see langword="null"/> for a non-tenanted entity, else a join and column
/// expression (the tenant column may live in the root table of a TPT hierarchy).
/// </param>
internal sealed record MaintenanceTarget(
    string Key,
    EncryptedMember Member,
    string Table,
    string QualifiedTableName,
    string PrimaryKeyColumn,
    RotationKeyKind KeyKind,
    string ValueColumn,
    string? BlindIndexColumn,
    (string Join, string Column)? TenantSource)
{
    /// <summary>Builds one target per distinct (table, column) holding an encrypted property of <paramref name="model"/>.</summary>
    public static List<MaintenanceTarget> Build(IModel model, ISqlGenerationHelper sql)
    {
        var targets = new Dictionary<string, MaintenanceTarget>(StringComparer.Ordinal);
        var metadata = EncryptionModelMetadata.For(model);

        foreach (var entityType in model.GetEntityTypes().OrderBy(e => e.Name, StringComparer.Ordinal))
        {
            foreach (var member in metadata.GetMembers(entityType))
            {
                foreach (var mapping in member.Property.GetTableColumnMappings())
                {
                    var table = mapping.Column.Table;
                    var key = $"{table.Schema ?? "public"}.{table.Name}.{mapping.Column.Name}";
                    if (targets.ContainsKey(key))
                        continue;

                    targets.Add(key, Create(key, member, entityType, table, mapping.Column.Name, sql));
                }
            }
        }

        return [.. targets.Values];
    }

    private static MaintenanceTarget Create(
        string key, EncryptedMember member, IEntityType entityType, ITable table, string column, ISqlGenerationHelper sql)
    {
        var keyProperty = entityType.FindPrimaryKey()!.Properties[0];
        var keyColumn = ColumnIn(keyProperty, table)
            ?? throw new InvalidOperationException($"'{member.DisplayName}': the primary key is not mapped to table '{table.Name}'.");
        var providerType = keyProperty.GetValueConverter()?.ProviderClrType ?? keyProperty.ClrType;
        var keyKind = RotationKeySupport.Classify(providerType)
            ?? throw new NotSupportedException($"'{member.DisplayName}': primary keys stored as '{providerType.Name}' are not supported.");

        string? blindIndexColumn = null;
        if (member.BlindIndexProperty is { } blindIndex)
        {
            blindIndexColumn = ColumnIn(blindIndex, table) is { } name
                ? sql.DelimitIdentifier(name)
                : throw new InvalidOperationException($"'{member.DisplayName}': the blind index is not mapped to table '{table.Name}'.");
        }

        (string, string)? tenantSource = null;
        if (typeof(IHasTenant).IsAssignableFrom(entityType.ClrType))
        {
            var tenantProperty = entityType.FindProperty(nameof(IHasTenant.TenantId))
                ?? throw new InvalidOperationException($"'{entityType.ShortName()}' implements IHasTenant but maps no TenantId.");

            if (ColumnIn(tenantProperty, table) is { } tenantColumn)
            {
                tenantSource = (string.Empty, "t." + sql.DelimitIdentifier(tenantColumn));
            }
            else
            {
                // TPT: the tenant column lives in another table of the hierarchy, joined on the shared key.
                var tenantMapping = tenantProperty.GetTableColumnMappings().First();
                var rootTable = tenantMapping.Column.Table;
                var rootKeyColumn = ColumnIn(keyProperty, rootTable)!;
                tenantSource = (
                    $" JOIN {Delimit(rootTable, sql)} AS r ON r.{sql.DelimitIdentifier(rootKeyColumn)} = t.{sql.DelimitIdentifier(keyColumn)}",
                    "r." + sql.DelimitIdentifier(tenantMapping.Column.Name));
            }
        }

        var qualified = table.Schema is null ? sql.DelimitIdentifier(table.Name) : sql.DelimitIdentifier(table.Name, table.Schema);
        return new MaintenanceTarget(
            key,
            member,
            Delimit(table, sql),
            qualified,
            sql.DelimitIdentifier(keyColumn),
            keyKind,
            sql.DelimitIdentifier(column),
            blindIndexColumn,
            tenantSource);
    }

    private static string? ColumnIn(IProperty property, ITable table) =>
        property.GetTableColumnMappings().FirstOrDefault(m => m.Column.Table == table)?.Column.Name;

    private static string Delimit(ITable table, ISqlGenerationHelper sql) =>
        table.Schema is null ? sql.DelimitIdentifier(table.Name) : sql.DelimitIdentifier(table.Name, table.Schema);
}
