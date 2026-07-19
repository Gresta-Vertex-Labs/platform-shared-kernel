using System.Globalization;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.QueryDsl;
using SharedKernel.Primitives.Results;
using SharedKernel.Search.Abstractions.Errors;
using SharedKernel.Search.Abstractions.Models;

namespace SharedKernel.Search.ElasticSearch.Querying;

/// <summary>
/// Compiles the closed 8-node <see cref="SearchFilter"/> hierarchy into an ElasticSearch
/// <see cref="Query"/> object graph, and injects the tenant predicate as the outermost filter clause.
/// </summary>
/// <remarks>
/// <para>
/// Object-initializer form throughout (<c>new Query { Bool = new BoolQuery { ... } }</c>), matching
/// the 9.0 breaking change that moved the container types <see cref="Query"/>,
/// <c>Elastic.Clients.Elasticsearch.Aggregations.Aggregation</c>, and <see cref="SortOptions"/> from
/// static factory methods to plain settable properties.
/// </para>
/// <para>
/// Every node lands in <see cref="BoolQuery.Filter"/> (or, for negation, <see cref="BoolQuery.MustNot"/>)
/// — filter context: non-scoring, cacheable, and semantically identical to Meilisearch's filter, which
/// has no scoring notion at all.
/// </para>
/// <para>
/// The client has no conditionless-query support — it serializes empty objects rather than eliding
/// them — so clause collections are built conditionally in C# and only non-empty ones are assigned. An
/// empty <see cref="AndFilter"/>/<see cref="OrFilter"/> therefore compiles to a clauseless
/// <see cref="BoolQuery"/>, which ElasticSearch itself treats as match-all — consistent with AND's and
/// (via the engine's should-clause default) OR's own logical identities.
/// </para>
/// <para>This is an independent declaration — never a shared base with <c>MeilisearchFilterCompiler</c>.</para>
/// </remarks>
internal static class ElasticSearchFilterCompiler
{
    /// <summary>Compiles <paramref name="filter"/> alone, with no tenant injection.</summary>
    public static Query Compile(SearchFilter filter) => filter switch
    {
        EqualFilter f => new Query { Term = new TermQuery { Field = f.Field, Value = ToFieldValue(f.Value) } },
        NotEqualFilter f => new Query
        {
            Bool = new BoolQuery
            {
                MustNot = [new Query { Term = new TermQuery { Field = f.Field, Value = ToFieldValue(f.Value) } }],
            },
        },
        InFilter f => new Query
        {
            Terms = new TermsQuery(f.Field, new TermsQueryField(f.Values.Select(ToFieldValue).ToArray())),
        },
        RangeFilter f => new Query { Range = CompileRange(f) },
        ExistsFilter f => new Query { Exists = new ExistsQuery(f.Field) },
        AndFilter f => CompileAnd(f),
        OrFilter f => CompileOr(f),
        NotFilter f => new Query { Bool = new BoolQuery { MustNot = [Compile(f.Operand)] } },
    };

    /// <summary>
    /// Compiles <paramref name="filter"/> (if any) and prepends the tenant predicate as the outermost
    /// filter clause. Fails closed — with no I/O — when <paramref name="definition"/> declares a
    /// <c>TenantField</c> and <paramref name="tenantScope"/> is <see cref="TenantScope.None"/>.
    /// </summary>
    public static Result<Query?> CompileWithTenantScope(
        SearchIndexDefinition definition, SearchFilter? filter, TenantScope tenantScope)
    {
        var callerClause = filter is null ? null : Compile(filter);

        if (definition.TenantField is not { } tenantField)
        {
            return Result<Query?>.Success(callerClause);
        }

        if (string.IsNullOrEmpty(tenantScope.Value))
        {
            return Result<Query?>.Failure(SearchErrors.TenantScopeMissing(definition.Name));
        }

        var tenantClause = new Query
        {
            Term = new TermQuery { Field = tenantField, Value = FieldValue.String(tenantScope.Value) },
        };

        if (callerClause is null)
        {
            return Result<Query?>.Success(tenantClause);
        }

        return Result<Query?>.Success(new Query { Bool = new BoolQuery { Filter = [tenantClause, callerClause] } });
    }

    private static Query CompileAnd(AndFilter filter)
    {
        var clauses = filter.Operands.Select(Compile).ToList();
        var boolQuery = new BoolQuery();
        if (clauses.Count > 0)
        {
            boolQuery.Filter = clauses;
        }

        return new Query { Bool = boolQuery };
    }

    private static Query CompileOr(OrFilter filter)
    {
        var clauses = filter.Operands.Select(Compile).ToList();
        var boolQuery = new BoolQuery();
        if (clauses.Count > 0)
        {
            // ElasticSearch defaults minimum_should_match to 1 for a bool query with should clauses
            // and no must/filter clauses — the documented OR semantics this relies on.
            boolQuery.Should = clauses;
        }

        return new Query { Bool = boolQuery };
    }

    private static IRangeQuery CompileRange(RangeFilter filter)
    {
        var isDate = filter.From is { Kind: SearchValueKind.DateTimeOffset }
            || filter.To is { Kind: SearchValueKind.DateTimeOffset };

        if (isDate)
        {
            var dateRange = new DateRangeQuery(filter.Field);
            if (filter.From is { } from)
            {
                var dateMath = ToDateMath(from);
                if (filter.FromInclusive)
                {
                    dateRange.Gte = dateMath;
                }
                else
                {
                    dateRange.Gt = dateMath;
                }
            }

            if (filter.To is { } to)
            {
                var dateMath = ToDateMath(to);
                if (filter.ToInclusive)
                {
                    dateRange.Lte = dateMath;
                }
                else
                {
                    dateRange.Lt = dateMath;
                }
            }

            return dateRange;
        }

        var numberRange = new NumberRangeQuery(filter.Field);
        if (filter.From is { } fromValue)
        {
            var value = ToDouble(fromValue);
            if (filter.FromInclusive)
            {
                numberRange.Gte = value;
            }
            else
            {
                numberRange.Gt = value;
            }
        }

        if (filter.To is { } toValue)
        {
            var value = ToDouble(toValue);
            if (filter.ToInclusive)
            {
                numberRange.Lte = value;
            }
            else
            {
                numberRange.Lt = value;
            }
        }

        return numberRange;
    }

    private static FieldValue ToFieldValue(SearchValue value) => value.Kind switch
    {
        SearchValueKind.String => FieldValue.String(value.AsString),
        SearchValueKind.Int64 => FieldValue.Long(value.AsInt64),
        SearchValueKind.Double => FieldValue.Double(value.AsDouble),
        SearchValueKind.Boolean => FieldValue.Boolean(value.AsBoolean),
        // Strict ISO-8601 for ElasticSearch date fields (Meilisearch instead uses Unix epoch seconds
        // in its own, separate filter compiler).
        SearchValueKind.DateTimeOffset => FieldValue.String(value.AsDateTimeOffset.ToString("O", CultureInfo.InvariantCulture)),
    };

    private static DateMath ToDateMath(SearchValue value) =>
        DateMath.FromString(value.AsDateTimeOffset.ToString("O", CultureInfo.InvariantCulture));

    private static double ToDouble(SearchValue value) => value.Kind switch
    {
        SearchValueKind.Int64 => value.AsInt64,
        SearchValueKind.Double => value.AsDouble,
        _ => throw new InvalidOperationException(
            $"Numeric range bound expected Int64 or Double but received {value.Kind}."),
    };
}
