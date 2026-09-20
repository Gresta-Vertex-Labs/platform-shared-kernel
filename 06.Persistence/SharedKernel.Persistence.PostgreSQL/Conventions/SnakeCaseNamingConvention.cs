using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;

namespace SharedKernel.Persistence.PostgreSQL.Conventions;

/// <summary>
/// An EF Core <see cref="IModelFinalizingConvention"/> that converts table names, column names
/// (including complex-type sub-columns), index names, primary/alternate-key constraint names, check
/// constraint names, and foreign-key constraint names to <c>snake_case</c>, truncating any name that
/// would exceed PostgreSQL's 63-byte identifier limit.
/// </summary>
/// <remarks>
/// <para>
/// Registered automatically when <c>UsePostgreSQL()</c> is called. The convention is added via
/// the DbContext's <c>ConfigureConventions(ModelConfigurationBuilder)</c> override.
/// </para>
/// <para>
/// Conversion is idempotent — names that are already lowercase and underscore-separated pass
/// through unchanged. <c>PascalCase</c>, <c>camelCase</c>, and
/// <c>UPPER_SNAKE_CASE</c> are all normalised to <c>lower_snake_case</c>, and a run of consecutive
/// capitals is treated as a single acronym word (<c>IPv4Address</c> → <c>ipv4_address</c>,
/// <c>HTTPRequest</c> → <c>http_request</c>).
/// </para>
/// <para>
/// <strong>Scope, and a deliberate omission:</strong> a non-table-backed entity type
/// (a keyless query type, a <c>ToView</c>/<c>ToSqlQuery</c>/<c>ToFunction</c> mapping, an abstract TPC
/// root with no table of its own) is skipped entirely — <c>GetTableName()</c> returning
/// <see langword="null"/> for such a type is deliberate EF Core behaviour this convention must not
/// override. <strong>Sequence names are NOT renamed</strong> — a <c>Sequence</c>'s
/// <see cref="Microsoft.EntityFrameworkCore.Metadata.IReadOnlySequence.Name"/> doubles as its lookup
/// key for any property configured with <c>UseSequence(name)</c>; renaming it here, after that
/// binding already resolved by name, is a correctness hazard this convention declines to take on. A
/// service wanting a snake_case sequence name should simply name the sequence in snake_case at its
/// own <c>HasSequence(...)</c> call site.
/// </para>
/// </remarks>
internal sealed class SnakeCaseNamingConvention : IModelFinalizingConvention
{
    // PostgreSQL's NAMEDATALEN is 64, leaving 63 usable bytes for an identifier — anything longer is
    // silently truncated by the server itself, which risks two distinct long names colliding on the
    // same truncated identifier. This convention truncates explicitly and appends a short stable hash
    // of the full original name so two colliding names get two DIFFERENT truncated identifiers.
    private const int MaxIdentifierBytes = 63;

    /// <inheritdoc />
    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
        {
            // Non-table-backed entity type (view/SqlQuery/function/keyless/abstract TPC root) —
            // GetTableName() returning null here is deliberate EF Core behaviour; do not invent one.
            if (entityType.GetTableName() is null)
                continue;

            entityType.SetTableName(Rename(entityType.GetTableName()!));

            foreach (var property in entityType.GetProperties())
            {
                var columnName = property.GetColumnName() ?? property.Name;
                property.SetColumnName(Rename(columnName));
            }

            // Complex-type properties — e.g. a Money property's Amount/Currency sub-columns.
            // entityType.GetProperties() above does not reach into a complex type's own properties; a
            // complex type can itself contain a further nested complex property (EF Core 10), so this
            // walks the whole tree, not just one level.
            foreach (var complexProperty in entityType.GetComplexProperties())
                RenameComplexTypeColumns(complexProperty);

            foreach (var index in entityType.GetIndexes())
            {
                var indexName = index.GetDatabaseName();
                if (!string.IsNullOrEmpty(indexName))
                    index.SetDatabaseName(Rename(indexName));
            }

            foreach (var fk in entityType.GetForeignKeys())
            {
                var constraintName = fk.GetConstraintName();
                if (!string.IsNullOrEmpty(constraintName))
                    fk.SetConstraintName(Rename(constraintName));
            }

            // Primary key and alternate keys.
            foreach (var key in entityType.GetKeys())
            {
                var keyName = key.GetName();
                if (!string.IsNullOrEmpty(keyName))
                    key.SetName(Rename(keyName));
            }

            // Check constraints.
            foreach (var checkConstraint in entityType.GetCheckConstraints())
            {
                var constraintName = checkConstraint.Name;
                if (!string.IsNullOrEmpty(constraintName))
                    checkConstraint.SetName(Rename(constraintName));
            }
        }
    }

    /// <summary>
    /// Converts every scalar property of <paramref name="complexProperty"/>'s complex type to
    /// <c>snake_case</c>, recursing into any further nested complex property.
    /// </summary>
    private static void RenameComplexTypeColumns(IConventionComplexProperty complexProperty)
    {
        var complexType = complexProperty.ComplexType;

        foreach (var property in complexType.GetProperties())
        {
            var columnName = property.GetColumnName() ?? property.Name;
            property.SetColumnName(Rename(columnName));
        }

        foreach (var nested in complexType.GetComplexProperties())
            RenameComplexTypeColumns(nested);
    }

    // Converts to snake_case, then truncates to PostgreSQL's 63-byte identifier limit.
    private static string Rename(string name) => Truncate(ToSnakeCase(name));

    /// <summary>
    /// Converts a <c>PascalCase</c>, <c>camelCase</c>, or already-snake-case identifier to
    /// <c>lower_snake_case</c>. Idempotent on already-snake-case input.
    /// </summary>
    public static string ToSnakeCase(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        // Insert underscore between a lowercase letter (or digit) followed by an uppercase letter.
        var result = Regex.Replace(name, @"([a-z0-9])([A-Z])", "$1_$2");
        // Insert underscore between consecutive uppercase letters followed by a lowercase letter —
        // treats a run of capitals as one acronym word (HTTPRequest -> HTTP_Request).
        result = Regex.Replace(result, @"([A-Z]+)([A-Z][a-z])", "$1_$2");

        return result.ToLowerInvariant();
    }

    /// <summary>
    /// Truncates <paramref name="name"/> to PostgreSQL's 63-byte identifier limit when it exceeds it,
    /// replacing the truncated tail with an 8-character stable hash of the FULL original name so two
    /// distinct long names that share the same 63-byte prefix still produce two DIFFERENT
    /// identifiers.
    /// </summary>
    public static string Truncate(string name)
    {
        if (Encoding.UTF8.GetByteCount(name) <= MaxIdentifierBytes)
            return name;

        var suffix = "_" + StableHash(name);
        int keepBytes = MaxIdentifierBytes - Encoding.UTF8.GetByteCount(suffix);

        return TruncateToByteLength(name, keepBytes) + suffix;
    }

    // Truncates a UTF-8 string to at most `maxBytes` bytes without splitting a multi-byte character.
    private static string TruncateToByteLength(string value, int maxBytes)
    {
        if (maxBytes <= 0)
            return string.Empty;

        var bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length <= maxBytes)
            return value;

        int length = maxBytes;
        // Back off while the boundary sits inside a multi-byte UTF-8 sequence (a continuation byte
        // has its top two bits set to 10).
        while (length > 0 && (bytes[length] & 0b1100_0000) == 0b1000_0000)
            length--;

        return Encoding.UTF8.GetString(bytes, 0, length);
    }

    // Deterministic FNV-1a 64-bit hash, rendered as 8 lowercase hex characters — stable across
    // processes/machines/.NET versions, unlike string.GetHashCode(). Migrations generated on one
    // machine must produce byte-identical DDL when regenerated on another.
    private static string StableHash(string value)
    {
        const ulong FnvOffsetBasis = 14695981039346656037UL;
        const ulong FnvPrime = 1099511628211UL;

        ulong hash = FnvOffsetBasis;
        foreach (byte b in Encoding.UTF8.GetBytes(value))
        {
            hash ^= b;
            hash *= FnvPrime;
        }

        return (hash & 0xFFFFFFFFUL).ToString("x8");
    }
}
