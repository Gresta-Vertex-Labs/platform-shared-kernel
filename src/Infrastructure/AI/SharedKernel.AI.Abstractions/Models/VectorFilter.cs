namespace SharedKernel.AI.Abstractions.Models;

/// <summary>
/// The closed, eight-node abstract-syntax-tree base for structured vector-metadata filter predicates.
/// </summary>
/// <remarks>
/// <para>
/// <b>Closed by construction, not by convention:</b> this base declares a <c>private protected</c>
/// constructor and every subtype's constructor is <c>internal</c>, so no assembly outside
/// <c>SharedKernel.AI.Abstractions</c> can construct a node — but the subtypes themselves are
/// <c>public</c>, so both provider packages can pattern-match and read them. This is what makes each
/// provider's translation switch an exhaustive C# switch with no default arm. Neither provider's
/// translation switch may carry a discard (<c>_ =&gt;</c>) arm.
/// </para>
/// <para>
/// The static factories on this base (<see cref="Eq"/>, <see cref="Ne"/>, <see cref="In"/>,
/// <see cref="Between"/>, <see cref="Exists"/>, <see cref="All"/>, <see cref="Any"/>,
/// <see cref="Negate"/>) are the <em>only</em> sanctioned construction path.
/// </para>
/// <para>
/// <b>Deliberate structural twin of <c>09.Search</c>'s <c>SearchFilter</c>:</b> same closed-by-
/// construction mechanism, same static-factory-only construction path. Both Qdrant and Milvus
/// genuinely, faithfully express all eight nodes — Equal/In/Range/And/Or map directly onto both
/// engines' native filter primitives; NotEqual/Not are synthesised via <c>must_not</c>-wrapping on
/// Qdrant (no native "not equal" match primitive) and directly via Milvus's boolean-expression
/// operators; Exists is <c>IsEmpty</c>-negation on Qdrant (verified against a real server —
/// <c>IsNull</c>-negation was the original assumption but is WRONG: Qdrant's <c>IsNullCondition</c>
/// matches only a payload key that is genuinely present with a JSON <see langword="null"/> value, never
/// an absent key, which makes its negation vacuously true for every record) and <c>IS NOT NULL</c> on
/// Milvus (requiring the field declared nullable at collection-creation time).
/// </para>
/// <para>
/// <b><see cref="Between"/> rejects <see cref="VectorValueKind.String"/> and
/// <see cref="VectorValueKind.Boolean"/> bounds — a platform-wide consistency choice:</b> Qdrant's
/// range condition is numeric/datetime only, with no lexicographic string-range primitive at all — a
/// hard impossibility on that engine, identical to <c>09.Search</c>'s reasoning. Milvus's expression
/// grammar can express a lexicographic <c>VARCHAR</c> range; it is deliberately left unused here for
/// platform-wide consistency with the identical rule in <c>09.Search</c>'s <c>SearchFilter</c>. Only
/// <see cref="VectorValueKind.Int64"/>, <see cref="VectorValueKind.Double"/>, and
/// <see cref="VectorValueKind.DateTimeOffset"/> are legal range bounds.
/// </para>
/// <para>
/// <b>What is deliberately absent, and why:</b> no free-text/full-text node — whatever "text search"
/// means here happens through the embedding <c>Vector</c> itself, upstream of this AST entirely. No
/// Fuzzy/TypoTolerance (not a metadata-filter concept on either engine). No Boost/FunctionScore
/// (relevance here <em>is</em> <c>VectorHit.Score</c>, already a documented hazard — a second,
/// filter-level boost knob would compound it). No GeoRadius (deferred, real-container verification
/// before guessing at semantics). No Prefix/Wildcard/Regex. No nested/object-array filter — same
/// correctness hazard <c>09.Search</c> identified (element-correlation loss on flattening engines); the
/// mandated portable technique is identical: flatten to a precomputed composite filterable field at
/// write time and filter it with <see cref="In"/>.
/// </para>
/// </remarks>
public abstract record VectorFilter
{
    private protected VectorFilter()
    {
    }

    /// <summary>Creates an equality predicate: <paramref name="field"/> equals <paramref name="value"/>.</summary>
    public static VectorFilter Eq(string field, VectorValue value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        return new EqualFilter(field, value);
    }

    /// <summary>Creates an inequality predicate: <paramref name="field"/> does not equal <paramref name="value"/>.</summary>
    public static VectorFilter Ne(string field, VectorValue value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        return new NotEqualFilter(field, value);
    }

    /// <summary>Creates a set-membership predicate: <paramref name="field"/> is one of <paramref name="values"/>.</summary>
    public static VectorFilter In(string field, params VectorValue[] values)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        ArgumentNullException.ThrowIfNull(values);
        return new InFilter(field, values.ToArray());
    }

    /// <summary>
    /// Creates a range predicate over <paramref name="field"/> bounded by <paramref name="from"/> and
    /// <paramref name="to"/> (either bound may be <see langword="null"/> for an open end).
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="from"/> or <paramref name="to"/> holds a <see cref="VectorValueKind.String"/>
    /// or <see cref="VectorValueKind.Boolean"/> value. Only <see cref="VectorValueKind.Int64"/>,
    /// <see cref="VectorValueKind.Double"/>, and <see cref="VectorValueKind.DateTimeOffset"/> are
    /// legal range bounds.
    /// </exception>
    public static VectorFilter Between(
        string field,
        VectorValue? from,
        VectorValue? to,
        bool fromInclusive = true,
        bool toInclusive = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        ThrowIfUnsupportedRangeBound(from, nameof(from));
        ThrowIfUnsupportedRangeBound(to, nameof(to));
        return new RangeFilter(field, from, fromInclusive, to, toInclusive);
    }

    /// <summary>Creates a field-existence predicate: <paramref name="field"/> is present on the record.</summary>
    public static VectorFilter Exists(string field)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        return new ExistsFilter(field);
    }

    /// <summary>Creates a conjunction (logical AND) of <paramref name="operands"/>.</summary>
    public static VectorFilter All(params VectorFilter[] operands)
    {
        ArgumentNullException.ThrowIfNull(operands);
        return new AndFilter(operands.ToArray());
    }

    /// <summary>Creates a disjunction (logical OR) of <paramref name="operands"/>.</summary>
    public static VectorFilter Any(params VectorFilter[] operands)
    {
        ArgumentNullException.ThrowIfNull(operands);
        return new OrFilter(operands.ToArray());
    }

    /// <summary>Creates a negation (logical NOT) of <paramref name="operand"/>.</summary>
    public static VectorFilter Negate(VectorFilter operand)
    {
        ArgumentNullException.ThrowIfNull(operand);
        return new NotFilter(operand);
    }

    private static void ThrowIfUnsupportedRangeBound(VectorValue? bound, string paramName)
    {
        if (bound is not { } value)
        {
            return;
        }

        if (value.Kind is VectorValueKind.String or VectorValueKind.Boolean)
        {
            throw new ArgumentException(
                $"VectorFilter.Between only accepts Int64, Double, or DateTimeOffset bounds; received {value.Kind}.",
                paramName);
        }
    }
}
