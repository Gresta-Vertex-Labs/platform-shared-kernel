namespace SharedKernel.Search.Abstractions.Models;

/// <summary>
/// The closed, eight-node abstract-syntax-tree base for structured search filter predicates.
/// </summary>
/// <remarks>
/// <para>
/// <b>Closed by construction, not by convention:</b> this base declares a <c>private protected</c>
/// constructor and every subtype's constructor is <c>internal</c>, so no assembly outside
/// <c>SharedKernel.Search.Abstractions</c> can construct a node — but the subtypes themselves are
/// <c>public</c>, so both provider packages can pattern-match and read them. This is what makes each
/// provider's translation switch an exhaustive C# switch with no default arm and structurally zero
/// <see cref="NotSupportedException"/> path. Neither provider's translation switch may carry a discard
/// (<c>_ =&gt;</c>) arm — compiler exhaustiveness plus the platform-wide
/// <c>TreatWarningsAsErrors</c> is the only thing preventing a future ninth node from silently
/// dropping on the lagging adapter.
/// </para>
/// <para>
/// The static factories on this base (<see cref="Eq"/>, <see cref="Ne"/>, <see cref="In"/>,
/// <see cref="Between"/>, <see cref="Exists"/>, <see cref="All"/>, <see cref="Any"/>,
/// <see cref="Negate"/>) are the <em>only</em> sanctioned construction path.
/// </para>
/// </remarks>
public abstract record SearchFilter
{
    private protected SearchFilter()
    {
    }

    /// <summary>Creates an equality predicate: <paramref name="field"/> equals <paramref name="value"/>.</summary>
    public static SearchFilter Eq(string field, SearchValue value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        return new EqualFilter(field, value);
    }

    /// <summary>Creates an inequality predicate: <paramref name="field"/> does not equal <paramref name="value"/>.</summary>
    public static SearchFilter Ne(string field, SearchValue value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        return new NotEqualFilter(field, value);
    }

    /// <summary>Creates a set-membership predicate: <paramref name="field"/> is one of <paramref name="values"/>.</summary>
    public static SearchFilter In(string field, params SearchValue[] values)
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
    /// <paramref name="from"/> or <paramref name="to"/> holds a <see cref="SearchValueKind.String"/>
    /// or <see cref="SearchValueKind.Boolean"/> value. Only <see cref="SearchValueKind.Int64"/>,
    /// <see cref="SearchValueKind.Double"/>, and <see cref="SearchValueKind.DateTimeOffset"/> are
    /// legal range bounds.
    /// </exception>
    public static SearchFilter Between(
        string field,
        SearchValue? from,
        SearchValue? to,
        bool fromInclusive = true,
        bool toInclusive = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        ThrowIfUnsupportedRangeBound(from, nameof(from));
        ThrowIfUnsupportedRangeBound(to, nameof(to));
        return new RangeFilter(field, from, fromInclusive, to, toInclusive);
    }

    /// <summary>Creates a field-existence predicate: <paramref name="field"/> is present on the document.</summary>
    public static SearchFilter Exists(string field)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        return new ExistsFilter(field);
    }

    /// <summary>Creates a conjunction (logical AND) of <paramref name="operands"/>.</summary>
    public static SearchFilter All(params SearchFilter[] operands)
    {
        ArgumentNullException.ThrowIfNull(operands);
        return new AndFilter(operands.ToArray());
    }

    /// <summary>Creates a disjunction (logical OR) of <paramref name="operands"/>.</summary>
    public static SearchFilter Any(params SearchFilter[] operands)
    {
        ArgumentNullException.ThrowIfNull(operands);
        return new OrFilter(operands.ToArray());
    }

    /// <summary>Creates a negation (logical NOT) of <paramref name="operand"/>.</summary>
    public static SearchFilter Negate(SearchFilter operand)
    {
        ArgumentNullException.ThrowIfNull(operand);
        return new NotFilter(operand);
    }

    private static void ThrowIfUnsupportedRangeBound(SearchValue? bound, string paramName)
    {
        if (bound is not { } value)
        {
            return;
        }

        if (value.Kind is SearchValueKind.String or SearchValueKind.Boolean)
        {
            throw new ArgumentException(
                $"SearchFilter.Between only accepts Int64, Double, or DateTimeOffset bounds; received {value.Kind}.",
                paramName);
        }
    }
}
