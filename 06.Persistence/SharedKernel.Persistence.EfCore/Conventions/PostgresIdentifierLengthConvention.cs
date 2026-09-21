using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace SharedKernel.Persistence.EfCore.Conventions;

/// <summary>
/// Keeps every table, column, key, index, foreign-key and check-constraint name within PostgreSQL's 63-byte
/// identifier limit, after snake_case naming (<c>EFCore.NamingConventions</c>) has been applied.
/// </summary>
/// <remarks>
/// <para>
/// PostgreSQL silently truncates a longer identifier, so two long names sharing a 63-byte prefix would collide
/// on the server. EF Core truncates its own <em>default</em> names to 63 characters, but snake_case renaming
/// runs on those names and adds underscores (so the result can exceed the limit again), and the limit is in
/// bytes, not characters. A name over the limit is cut at a UTF-8 character boundary and given an 8-character
/// stable hash of the full name, so two distinct long names always end up distinct.
/// </para>
/// <para>
/// Non-table-backed entity types (views, SQL queries, functions, keyless types) are skipped. Sequence names
/// are not touched: a sequence name is also its lookup key for <c>UseSequence(name)</c>.
/// </para>
/// </remarks>
internal sealed class PostgresIdentifierLengthConvention : IModelFinalizingConvention
{
    private const int MaxIdentifierBytes = 63;

    /// <inheritdoc />
    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
        {
            var tableName = entityType.GetTableName();
            if (tableName is null)
                continue;

            if (NeedsTruncation(tableName))
                entityType.SetTableName(Truncate(tableName));

            foreach (var property in entityType.GetProperties())
                TruncateColumn(property);

            foreach (var complexProperty in entityType.GetComplexProperties())
                TruncateComplexTypeColumns(complexProperty);

            foreach (var index in entityType.GetIndexes())
            {
                var name = index.GetDatabaseName();
                if (name is not null && NeedsTruncation(name))
                    index.SetDatabaseName(Truncate(name));
            }

            foreach (var foreignKey in entityType.GetForeignKeys())
            {
                var name = foreignKey.GetConstraintName();
                if (name is not null && NeedsTruncation(name))
                    foreignKey.SetConstraintName(Truncate(name));
            }

            foreach (var key in entityType.GetKeys())
            {
                var name = key.GetName();
                if (name is not null && NeedsTruncation(name))
                    key.SetName(Truncate(name));
            }

            foreach (var checkConstraint in entityType.GetCheckConstraints())
            {
                var name = checkConstraint.Name;
                if (name is not null && NeedsTruncation(name))
                    checkConstraint.SetName(Truncate(name));
            }
        }
    }

    private static void TruncateComplexTypeColumns(IConventionComplexProperty complexProperty)
    {
        foreach (var property in complexProperty.ComplexType.GetProperties())
            TruncateColumn(property);

        foreach (var nested in complexProperty.ComplexType.GetComplexProperties())
            TruncateComplexTypeColumns(nested);
    }

    private static void TruncateColumn(IConventionProperty property)
    {
        var columnName = property.GetColumnName();
        if (columnName is not null && NeedsTruncation(columnName))
            property.SetColumnName(Truncate(columnName));
    }

    private static bool NeedsTruncation(string name) => Encoding.UTF8.GetByteCount(name) > MaxIdentifierBytes;

    /// <summary>
    /// Returns <paramref name="name"/> unchanged when it fits in 63 UTF-8 bytes; otherwise a prefix cut at a
    /// character boundary plus <c>_</c> and an 8-character FNV-1a hash of the full name.
    /// </summary>
    internal static string Truncate(string name)
    {
        if (!NeedsTruncation(name))
            return name;

        var suffix = "_" + StableHash(name);
        var keepBytes = MaxIdentifierBytes - Encoding.UTF8.GetByteCount(suffix);

        return TruncateToByteLength(name, keepBytes) + suffix;
    }

    private static string TruncateToByteLength(string value, int maxBytes)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length <= maxBytes)
            return value;

        var length = maxBytes;

        // Back off while the cut would land inside a multi-byte UTF-8 sequence (continuation bytes are 10xxxxxx).
        while (length > 0 && (bytes[length] & 0b1100_0000) == 0b1000_0000)
            length--;

        return Encoding.UTF8.GetString(bytes, 0, length);
    }

    // Deterministic FNV-1a hash rendered as 8 lowercase hex characters: stable across processes, machines and
    // .NET versions (unlike string.GetHashCode()), so regenerated migrations produce identical DDL.
    private static string StableHash(string value)
    {
        const ulong FnvOffsetBasis = 14695981039346656037UL;
        const ulong FnvPrime = 1099511628211UL;

        var hash = FnvOffsetBasis;
        foreach (var b in Encoding.UTF8.GetBytes(value))
        {
            hash ^= b;
            hash *= FnvPrime;
        }

        return (hash & 0xFFFFFFFFUL).ToString("x8", System.Globalization.CultureInfo.InvariantCulture);
    }
}
