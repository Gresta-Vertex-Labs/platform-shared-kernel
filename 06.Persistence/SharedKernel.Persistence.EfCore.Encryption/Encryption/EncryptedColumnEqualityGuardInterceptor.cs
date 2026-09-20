using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using SharedKernel.Persistence.EfCore.Extensibility;

namespace SharedKernel.Persistence.EfCore.Encryption;

/// <summary>
/// Fails loudly, before executing it, a generated SQL command that appears to filter an encrypted column by
/// equality without going through its blind index.
/// </summary>
/// <remarks>
/// <para>
/// An encrypted property has no <c>ValueConverter</c> (see <see cref="EncryptionInterceptor"/>'s remarks), so
/// nothing stops <c>.Where(x =&gt; x.Email == "value")</c> from compiling and translating straight to
/// <c>WHERE "email" = @p0</c> — comparing plaintext to a column that holds ciphertext, which always returns zero
/// rows, never an error. That silent-empty-result failure mode is worse than throwing: it looks like "no match"
/// rather than "this query cannot work as written".
/// </para>
/// <para>
/// <strong>Heuristic, not exhaustive.</strong> This interceptor inspects the final, provider-generated SQL text for
/// each encrypted-and-not-blind-indexed column's name (quoted or not — <c>"." + columnName</c>, optionally quoted)
/// immediately followed by <c>=</c> or <c>IN</c> — the shape a direct equality/`Contains` filter produces. It does
/// not understand SQL, so an unusual
/// query shape (a computed column alias, a raw <c>FromSqlRaw</c> fragment that happens to reuse the same delimited
/// name for an unrelated purpose, …) can evade it in either direction — a false negative is silently permitted, a
/// false positive throws a query that mentions the column for some other legitimate reason. Treat this as a safety
/// net that catches the common mistake, not a proof of absence for the whole space of queries that reach a
/// database. Add <c>.WithBlindIndex()</c> and query via
/// <c>SharedKernel.Persistence.EfCore.Encryption.BlindIndex.EncryptedPropertyQueryExtensions.WhereBlindIndexEquals</c>
/// to search by an encrypted property's value.
/// </para>
/// <para>Registered as a stable singleton, the same way as <see cref="EncryptionInterceptor"/> — see <see cref="EncryptionInterceptorOptionsContributor"/>.</para>
/// <para>
/// <strong>Escape hatch:</strong> a query tagged with <see cref="DisableTagText"/> — via
/// <c>.TagWith(EncryptedColumnEqualityGuardInterceptor.DisableTagText)</c> — is never checked. The heuristic
/// above is best-effort; a consumer who hits a genuine false positive (the query text happens to match the
/// pattern for a reason unrelated to filtering the encrypted column) can opt that one query out explicitly,
/// rather than being unable to run it at all.
/// </para>
/// </remarks>
public sealed class EncryptedColumnEqualityGuardInterceptor : DbCommandInterceptor
{
    /// <summary>
    /// Tag text that opts one query out of this guard — pass to <c>.TagWith(...)</c> on the query, e.g.
    /// <c>context.Customers.TagWith(EncryptedColumnEqualityGuardInterceptor.DisableTagText).Where(...)</c>.
    /// EF Core embeds tag text verbatim as a SQL comment in the generated command, which this interceptor checks
    /// for before running its column-name heuristic.
    /// </summary>
    public const string DisableTagText = "SharedKernel:Persistence:Encryption:AllowEncryptedColumnEquality";

    private readonly ConditionalWeakTable<IModel, (string ColumnName, bool HasBlindIndex, Regex Pattern)[]> _guardedColumnsByModel = new();

    /// <inheritdoc />
    public override InterceptionResult<System.Data.Common.DbDataReader> ReaderExecuting(
        System.Data.Common.DbCommand command,
        CommandEventData eventData,
        InterceptionResult<System.Data.Common.DbDataReader> result)
    {
        Check(command, eventData);
        return result;
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<System.Data.Common.DbDataReader>> ReaderExecutingAsync(
        System.Data.Common.DbCommand command,
        CommandEventData eventData,
        InterceptionResult<System.Data.Common.DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Check(command, eventData);
        return ValueTask.FromResult(result);
    }

    private void Check(System.Data.Common.DbCommand command, CommandEventData eventData)
    {
        if (eventData.Context is not { } context || string.IsNullOrEmpty(command.CommandText))
            return;

        if (command.CommandText.Contains(DisableTagText, StringComparison.Ordinal))
            return;

        var guardedColumns = _guardedColumnsByModel.GetValue(context.Model, static model => BuildGuardedColumnList(model));

        foreach (var (columnName, hasBlindIndex, pattern) in guardedColumns)
        {
            if (!pattern.IsMatch(command.CommandText))
                continue;

            var remedy = hasBlindIndex
                ? "Query with EncryptedPropertyQueryExtensions.WhereBlindIndexEquals instead of a direct equality comparison."
                : "Add '.WithBlindIndex()' to the property, then query with EncryptedPropertyQueryExtensions.WhereBlindIndexEquals instead.";

            throw new InvalidOperationException(
                $"A generated query appears to filter encrypted column '{columnName}' by equality. This " +
                $"column holds ciphertext, so the filter can never match. {remedy}");
        }
    }

    // Matches "<table-alias-or-nothing>.<column>" (the shape every provider-generated column reference takes,
    // quoted or not) immediately followed by optional whitespace and "=" or the word "IN" — the shape a direct
    // equality/Contains filter compiles to. Deliberately independent of ISqlGenerationHelper.DelimitIdentifier,
    // whose quoting is provider- and identifier-dependent (a simple lowercase identifier is often left
    // unquoted) and therefore cannot be relied on to match the generated SQL text verbatim.
    //
    // A blind-indexed column is GUARDED TOO, not exempted: it is exactly the column a caller is most likely to
    // reach for a naive '==' against, since (unlike a non-indexed encrypted column) it visibly LOOKS searchable.
    // The regex only ever matches the ENCRYPTED column's own physical name (e.g. "email"), never the separate
    // blind-index shadow column's name (e.g. "email_blind_index") that WhereBlindIndexEquals itself compiles a
    // filter against — the two are different columns, so guarding the former can never false-positive on the
    // latter.
    private static (string ColumnName, bool HasBlindIndex, Regex Pattern)[] BuildGuardedColumnList(IModel model)
    {
        var columns = new List<(string, bool, Regex)>();

        void AddIfGuarded(IProperty property)
        {
            if (property.FindAnnotation(PersistenceModelAnnotationNames.Encrypt) is null)
                return;

            var columnName = property.GetColumnName();
            if (columnName is null || columns.Exists(c => c.Item1 == columnName))
                return;

            var hasBlindIndex = property.FindAnnotation(PropertyBuilderEncryptExtensions.BlindIndexAnnotationKey)?.Value is true;
            var pattern = new Regex(
                $@"\.""?{Regex.Escape(columnName)}""?\s*(=|IN\b)",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);
            columns.Add((columnName, hasBlindIndex, pattern));
        }

        foreach (var entityType in model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
                AddIfGuarded(property);

            // EF Core 10 complex-type sub-properties are flattened onto the same table — their columns need the
            // identical guard as a direct property's.
            foreach (var complexProperty in entityType.GetComplexProperties())
            {
                foreach (var property in complexProperty.ComplexType.GetProperties())
                    AddIfGuarded(property);
            }
        }

        return [.. columns];
    }
}
