using Qdrant.Client.Grpc;
using SharedKernel.AI.Abstractions.Models;
using SharedKernel.Execution.Tenancy;

namespace SharedKernel.AI.Qdrant.Querying;

/// <summary>
/// Translates the closed 8-node <see cref="VectorFilter"/> AST into Qdrant's native
/// <see cref="Filter"/>/<see cref="Condition"/> grammar via an exhaustive property-pattern switch with
/// <b>no discard arm</b>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Condition"/> can itself wrap an entire nested <see cref="Filter"/> (its own
/// <c>Must</c>/<c>Should</c>/<c>MustNot</c>), which is what makes arbitrary <c>And</c>/<c>Or</c>/<c>Not</c>
/// composition possible from a single per-node <see cref="Condition"/> return type.
/// </para>
/// <para>
/// <b>Equality/inequality on <see cref="VectorValueKind.Double"/> and
/// <see cref="VectorValueKind.DateTimeOffset"/> is synthesised via a degenerate Range/DatetimeRange
/// (<c>Gte == Lte == value</c>)</b> — Qdrant's <see cref="Match"/> primitive supports only
/// keyword/integer/boolean equality, with no native double or datetime equality match. This is
/// composition, not degradation — the same class of honest synthesis this domain's own contract
/// already documents for <c>NotEqual</c>/<c>Not</c> via <c>must_not</c>-wrapping.
/// </para>
/// <para>
/// <b><see cref="VectorFilter.In"/> is synthesised as a <c>should</c> (OR) of per-value equality
/// conditions when the values are not uniformly <see cref="VectorValueKind.String"/> or
/// <see cref="VectorValueKind.Int64"/></b> — for a uniform-kind string/integer set, the native
/// <see cref="Match.Keywords"/>/<see cref="Match.Integers"/> repeated-match primitive is used instead,
/// which is both more efficient and Qdrant's own idiomatic in-set primitive.
/// </para>
/// <para>
/// <b><see cref="VectorFilter.Exists"/> is <c>IsEmpty</c>-negation, never <c>IsNull</c>-negation</b> —
/// verified empirically against a real Qdrant server, not assumed from either primitive's name.
/// Qdrant's <see cref="IsNullCondition"/> matches only a payload key that is genuinely PRESENT with a
/// JSON <c>null</c> value; it does <b>not</b> match a key that is entirely absent from the payload, so
/// <c>must_not: [is_null(field)]</c> is vacuously true for every record regardless of whether the field
/// was ever set — a confirmed, fixed defect in an earlier implementation pass (every record matched
/// <c>Exists</c>, including ones that never declared the field at all). Qdrant's
/// <see cref="IsEmptyCondition"/> matches a key that is absent OR an empty array OR (per Qdrant's own
/// documented semantics) a JSON <c>null</c> value — its negation, <c>must_not: [is_empty(field)]</c>,
/// is therefore the correct "the field is genuinely present with a real value" translation.
/// </para>
/// </remarks>
internal static class QdrantFilterCompiler
{
    /// <summary>
    /// Compiles <paramref name="filter"/> (or an unconstrained filter, when <see langword="null"/>)
    /// together with the mandatory tenant-scope conjunction into one top-level <see cref="Filter"/>.
    /// </summary>
    /// <remarks>
    /// The tenant clause is injected as the <b>outermost</b> conjunction, strictly after
    /// <paramref name="filter"/> is translated — the caller-supplied filter can never omit it. This
    /// method assumes the caller has already fail-closed on <c>TenantScope.Global</c> against a
    /// tenant-declaring collection; it does not repeat that check.
    /// </remarks>
    public static Filter Compile(VectorFilter? filter, string? tenantField, TenantScope tenantScope)
    {
        var top = new Filter();

        if (filter is not null)
        {
            top.Must.Add(CompileNode(filter));
        }

        if (tenantField is not null && !tenantScope.IsGlobal)
        {
            top.Must.Add(BuildEqualityCondition(tenantField, VectorValue.From(tenantScope.Tenant!.Value.ToString())));
        }

        return top;
    }

    private static Condition CompileNode(VectorFilter node) => node switch
    {
        EqualFilter equal => BuildEqualityCondition(equal.Field, equal.Value),
        NotEqualFilter notEqual => WrapAsNestedFilter(f => f.MustNot.Add(BuildEqualityCondition(notEqual.Field, notEqual.Value))),
        InFilter inFilter => CompileIn(inFilter),
        RangeFilter range => CompileRange(range),
        ExistsFilter exists => WrapAsNestedFilter(f => f.MustNot.Add(new Condition { IsEmpty = new IsEmptyCondition { Key = exists.Field } })),
        AndFilter and => WrapAsNestedFilter(f => f.Must.AddRange(and.Operands.Select(CompileNode))),
        OrFilter or => WrapAsNestedFilter(f => f.Should.AddRange(or.Operands.Select(CompileNode))),
        NotFilter not => WrapAsNestedFilter(f => f.MustNot.Add(CompileNode(not.Operand))),
    };

    private static Condition CompileIn(InFilter inFilter)
    {
        if (inFilter.Values.Count > 0 && inFilter.Values.All(v => v.Kind == VectorValueKind.String))
        {
            var match = new Match { Keywords = new RepeatedStrings() };
            match.Keywords.Strings.AddRange(inFilter.Values.Select(v => v.AsString));
            return new Condition { Field = new FieldCondition { Key = inFilter.Field, Match = match } };
        }

        if (inFilter.Values.Count > 0 && inFilter.Values.All(v => v.Kind == VectorValueKind.Int64))
        {
            var match = new Match { Integers = new RepeatedIntegers() };
            match.Integers.Integers.AddRange(inFilter.Values.Select(v => v.AsInt64));
            return new Condition { Field = new FieldCondition { Key = inFilter.Field, Match = match } };
        }

        return WrapAsNestedFilter(f => f.Should.AddRange(
            inFilter.Values.Select(v => BuildEqualityCondition(inFilter.Field, v))));
    }

    private static Condition CompileRange(RangeFilter range)
    {
        var kind = range.From?.Kind ?? range.To?.Kind ?? VectorValueKind.Double;

        if (kind == VectorValueKind.DateTimeOffset)
        {
            var datetimeRange = new DatetimeRange();
            if (range.From is { } from)
            {
                var timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(from.AsDateTimeOffset);
                if (range.FromInclusive)
                {
                    datetimeRange.Gte = timestamp;
                }
                else
                {
                    datetimeRange.Gt = timestamp;
                }
            }

            if (range.To is { } to)
            {
                var timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(to.AsDateTimeOffset);
                if (range.ToInclusive)
                {
                    datetimeRange.Lte = timestamp;
                }
                else
                {
                    datetimeRange.Lt = timestamp;
                }
            }

            return new Condition { Field = new FieldCondition { Key = range.Field, DatetimeRange = datetimeRange } };
        }

        // Fully-qualified with `global::` — this file's own enclosing namespace ends in "...AI.Qdrant",
        // which collides with the "Qdrant.Client.Grpc" namespace's leading "Qdrant" segment under C#'s
        // relative namespace resolution; `global::` forces resolution from the true root instead.
        var numericRange = new global::Qdrant.Client.Grpc.Range();
        if (range.From is { } fromValue)
        {
            var value = ToDouble(fromValue);
            if (range.FromInclusive)
            {
                numericRange.Gte = value;
            }
            else
            {
                numericRange.Gt = value;
            }
        }

        if (range.To is { } toValue)
        {
            var value = ToDouble(toValue);
            if (range.ToInclusive)
            {
                numericRange.Lte = value;
            }
            else
            {
                numericRange.Lt = value;
            }
        }

        return new Condition { Field = new FieldCondition { Key = range.Field, Range = numericRange } };
    }

    private static double ToDouble(VectorValue value) => value.Kind switch
    {
        VectorValueKind.Int64 => value.AsInt64,
        VectorValueKind.Double => value.AsDouble,
        _ => throw new InvalidOperationException($"Range bounds must be Int64, Double, or DateTimeOffset; received {value.Kind}."),
    };

    private static Condition BuildEqualityCondition(string field, VectorValue value) => value.Kind switch
    {
        VectorValueKind.String => new Condition { Field = new FieldCondition { Key = field, Match = new Match { Keyword = value.AsString } } },
        VectorValueKind.Int64 => new Condition { Field = new FieldCondition { Key = field, Match = new Match { Integer = value.AsInt64 } } },
        VectorValueKind.Boolean => new Condition { Field = new FieldCondition { Key = field, Match = new Match { Boolean = value.AsBoolean } } },
        VectorValueKind.Double => new Condition
        {
            Field = new FieldCondition { Key = field, Range = new global::Qdrant.Client.Grpc.Range { Gte = value.AsDouble, Lte = value.AsDouble } },
        },
        VectorValueKind.DateTimeOffset => new Condition
        {
            Field = new FieldCondition
            {
                Key = field,
                DatetimeRange = new DatetimeRange
                {
                    Gte = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(value.AsDateTimeOffset),
                    Lte = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(value.AsDateTimeOffset),
                },
            },
        },
        _ => throw new InvalidOperationException($"Unknown VectorValueKind '{value.Kind}'."),
    };

    private static Condition WrapAsNestedFilter(Action<Filter> configure)
    {
        var filter = new Filter();
        configure(filter);
        return new Condition { Filter = filter };
    }
}
