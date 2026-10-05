using System.Globalization;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Results;
using SharedKernel.Search.Abstractions.Errors;
using SharedKernel.Search.Abstractions.Models;

namespace SharedKernel.Search.Meilisearch.Querying;

/// <summary>
/// Compiles the closed 8-node <see cref="SearchFilter"/> hierarchy into a Meilisearch filter-expression
/// string, and injects the tenant predicate as the outermost <c>AND</c>.
/// </summary>
/// <remarks>
/// <para>
/// Every <see cref="string"/> value is escaped and quoted; numerics and booleans are emitted bare;
/// <see cref="DateTimeOffset"/> is emitted as Unix epoch seconds — Meilisearch's filter DSL compares
/// numerics, not ISO-8601 strings.
/// </para>
/// <para>
/// Parentheses are emitted around every <c>Or</c> and <c>Not</c> node unconditionally (each such node
/// wraps its own compiled output), rather than relying on Meilisearch's <c>NOT &gt; AND &gt; OR</c>
/// precedence — precedence-dependent output is the classic filter-injection bug.
/// </para>
/// </remarks>
internal static class MeilisearchFilterCompiler
{
    /// <summary>Compiles <paramref name="filter"/> alone, with no tenant injection.</summary>
    /// <remarks>
    /// This switch intentionally carries no discard (<c>_ =&gt;</c>) arm — see
    /// <c>src/Infrastructure/Search/CLAUDE.md</c>, "Reconciling switch exhaustiveness with <c>TreatWarningsAsErrors</c>",
    /// for why the resulting CS8509 is downgraded via <c>WarningsNotAsErrors</c> at the project level
    /// rather than silenced by a catch-all arm here.
    /// </remarks>
    public static string Compile(SearchFilter filter) => filter switch
    {
        EqualFilter f => $"{f.Field} = {FormatValue(f.Value)}",
        NotEqualFilter f => $"{f.Field} != {FormatValue(f.Value)}",
        InFilter f => $"{f.Field} IN [{string.Join(", ", f.Values.Select(FormatValue))}]",
        RangeFilter f => CompileRange(f),
        ExistsFilter f => $"{f.Field} EXISTS",
        AndFilter f => string.Join(" AND ", f.Operands.Select(Compile)),
        OrFilter f => $"({string.Join(" OR ", f.Operands.Select(Compile))})",
        NotFilter f => $"NOT ({Compile(f.Operand)})",
    };

    /// <summary>
    /// Compiles <paramref name="filter"/> (if any) and prepends the tenant predicate as the outermost
    /// <c>AND</c>, structurally beyond the caller's reach. Fails closed — with no I/O — when
    /// <paramref name="definition"/> declares a <c>TenantField</c> and <paramref name="tenantScope"/>
    /// is <see cref="TenantScope.Global"/>.
    /// </summary>
    public static Result<string?> CompileWithTenantScope(
        SearchIndexDefinition definition, SearchFilter? filter, TenantScope tenantScope)
    {
        string? callerClause = filter is null ? null : Compile(filter);
        if (string.IsNullOrEmpty(callerClause))
        {
            callerClause = null;
        }

        if (definition.TenantField is not { } tenantField)
        {
            return Result<string?>.Success(callerClause);
        }

        if (tenantScope.IsGlobal)
        {
            return Result<string?>.Failure(SearchErrors.TenantScopeMissing(definition.Name));
        }

        var tenantClause = $"{tenantField} = {FormatValue(SearchValue.From(tenantScope.Tenant!.Value.ToString()))}";
        var combined = callerClause is null ? tenantClause : $"({tenantClause}) AND ({callerClause})";
        return Result<string?>.Success(combined);
    }

    private static string CompileRange(RangeFilter filter)
    {
        var clauses = new List<string>(2);

        if (filter.From is { } from)
        {
            var op = filter.FromInclusive ? ">=" : ">";
            clauses.Add($"{filter.Field} {op} {FormatValue(from)}");
        }

        if (filter.To is { } to)
        {
            var op = filter.ToInclusive ? "<=" : "<";
            clauses.Add($"{filter.Field} {op} {FormatValue(to)}");
        }

        return clauses.Count switch
        {
            0 => throw new InvalidOperationException(
                $"RangeFilter on field '{filter.Field}' has neither a From nor a To bound."),
            1 => clauses[0],
            _ => $"({clauses[0]}) AND ({clauses[1]})",
        };
    }

    // This switch intentionally carries no discard (`_ =>`) arm, covering all five declared
    // SearchValueKind members exhaustively. The C# compiler still emits CS8524 because an enum's
    // underlying integral representation always permits an unnamed value — see src/Infrastructure/Search/CLAUDE.md,
    // "Reconciling switch exhaustiveness with TreatWarningsAsErrors", for why that warning is
    // downgraded via WarningsNotAsErrors at the project level rather than silenced here.
    private static string FormatValue(SearchValue value) => value.Kind switch
    {
        SearchValueKind.String => QuoteAndEscape(value.AsString),
        SearchValueKind.Int64 => value.AsInt64.ToString(CultureInfo.InvariantCulture),
        SearchValueKind.Double => value.AsDouble.ToString(CultureInfo.InvariantCulture),
        SearchValueKind.Boolean => value.AsBoolean ? "true" : "false",
        SearchValueKind.DateTimeOffset => value.AsDateTimeOffset.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
    };

    private static string QuoteAndEscape(string value)
    {
        var escaped = value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        return $"\"{escaped}\"";
    }
}
