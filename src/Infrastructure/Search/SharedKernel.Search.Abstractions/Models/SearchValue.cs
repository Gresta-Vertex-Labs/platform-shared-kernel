using System.Globalization;

namespace SharedKernel.Search.Abstractions.Models;

/// <summary>
/// A closed, five-kind scalar union used as the only value type <see cref="SearchFilter"/> nodes may
/// carry — the direct antidote to typing a filter value as <c>object</c> or <c>dynamic</c>.
/// </summary>
/// <remarks>
/// <para>
/// Construct via the <see cref="From(string)"/> family of static factories or the implicit
/// conversion operators — there is no public constructor. Read back via the kind-checked
/// <see cref="AsString"/>/<see cref="AsInt64"/>/<see cref="AsDouble"/>/<see cref="AsBoolean"/>/
/// <see cref="AsDateTimeOffset"/> accessors; reading the wrong accessor for the current
/// <see cref="Kind"/> throws <see cref="InvalidOperationException"/> — an adapter programming error,
/// never a consumer-facing expected failure.
/// </para>
/// <para>
/// <see cref="From(Guid)"/> normalises to the canonical <c>"D"</c> string form (lowercase, hyphenated,
/// no braces) so a <see cref="Guid"/> filter value is identical on both engines.
/// </para>
/// <para>
/// <b>Timestamp encoding is provider-specific and is applied by each provider's own translator, not
/// here:</b> <see cref="DateTimeOffset"/> serialises to Unix epoch seconds for Meilisearch (whose
/// filter DSL compares numerics, not ISO-8601 strings) and to strict ISO-8601 for ElasticSearch date
/// fields.
/// </para>
/// </remarks>
public readonly record struct SearchValue
{
    private readonly string? _stringValue;
    private readonly long _int64Value;
    private readonly double _doubleValue;
    private readonly bool _booleanValue;
    private readonly DateTimeOffset _dateTimeOffsetValue;

    private SearchValue(
        SearchValueKind kind,
        string? stringValue = null,
        long int64Value = 0,
        double doubleValue = 0,
        bool booleanValue = false,
        DateTimeOffset dateTimeOffsetValue = default)
    {
        Kind = kind;
        _stringValue = stringValue;
        _int64Value = int64Value;
        _doubleValue = doubleValue;
        _booleanValue = booleanValue;
        _dateTimeOffsetValue = dateTimeOffsetValue;
    }

    /// <summary>Gets the scalar kind currently held by this value.</summary>
    public SearchValueKind Kind { get; }

    /// <summary>Gets the held value as a <see cref="string"/>.</summary>
    /// <exception cref="InvalidOperationException"><see cref="Kind"/> is not <see cref="SearchValueKind.String"/>.</exception>
    public string AsString => Kind == SearchValueKind.String
        ? _stringValue!
        : throw new InvalidOperationException($"SearchValue holds {Kind}, not {SearchValueKind.String}.");

    /// <summary>Gets the held value as an <see cref="long"/>.</summary>
    /// <exception cref="InvalidOperationException"><see cref="Kind"/> is not <see cref="SearchValueKind.Int64"/>.</exception>
    public long AsInt64 => Kind == SearchValueKind.Int64
        ? _int64Value
        : throw new InvalidOperationException($"SearchValue holds {Kind}, not {SearchValueKind.Int64}.");

    /// <summary>Gets the held value as a <see cref="double"/>.</summary>
    /// <exception cref="InvalidOperationException"><see cref="Kind"/> is not <see cref="SearchValueKind.Double"/>.</exception>
    public double AsDouble => Kind == SearchValueKind.Double
        ? _doubleValue
        : throw new InvalidOperationException($"SearchValue holds {Kind}, not {SearchValueKind.Double}.");

    /// <summary>Gets the held value as a <see cref="bool"/>.</summary>
    /// <exception cref="InvalidOperationException"><see cref="Kind"/> is not <see cref="SearchValueKind.Boolean"/>.</exception>
    public bool AsBoolean => Kind == SearchValueKind.Boolean
        ? _booleanValue
        : throw new InvalidOperationException($"SearchValue holds {Kind}, not {SearchValueKind.Boolean}.");

    /// <summary>Gets the held value as a <see cref="DateTimeOffset"/>.</summary>
    /// <exception cref="InvalidOperationException"><see cref="Kind"/> is not <see cref="SearchValueKind.DateTimeOffset"/>.</exception>
    public DateTimeOffset AsDateTimeOffset => Kind == SearchValueKind.DateTimeOffset
        ? _dateTimeOffsetValue
        : throw new InvalidOperationException($"SearchValue holds {Kind}, not {SearchValueKind.DateTimeOffset}.");

    /// <summary>Creates a <see cref="SearchValueKind.String"/> value.</summary>
    public static SearchValue From(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new SearchValue(SearchValueKind.String, stringValue: value);
    }

    /// <summary>Creates a <see cref="SearchValueKind.Int64"/> value.</summary>
    public static SearchValue From(long value) => new(SearchValueKind.Int64, int64Value: value);

    /// <summary>Creates a <see cref="SearchValueKind.Double"/> value.</summary>
    public static SearchValue From(double value) => new(SearchValueKind.Double, doubleValue: value);

    /// <summary>Creates a <see cref="SearchValueKind.Boolean"/> value.</summary>
    public static SearchValue From(bool value) => new(SearchValueKind.Boolean, booleanValue: value);

    /// <summary>Creates a <see cref="SearchValueKind.DateTimeOffset"/> value.</summary>
    public static SearchValue From(DateTimeOffset value) => new(SearchValueKind.DateTimeOffset, dateTimeOffsetValue: value);

    /// <summary>
    /// Creates a <see cref="SearchValueKind.String"/> value from a <see cref="Guid"/>, normalised to
    /// its canonical <c>"D"</c> string form (lowercase, hyphenated, no braces) — identical on both
    /// engines.
    /// </summary>
    public static SearchValue From(Guid value) => From(value.ToString("D", CultureInfo.InvariantCulture));

    /// <summary>Implicitly converts a <see cref="string"/> to a <see cref="SearchValue"/>.</summary>
    public static implicit operator SearchValue(string value) => From(value);

    /// <summary>Implicitly converts an <see cref="int"/> to a <see cref="SearchValue"/>.</summary>
    public static implicit operator SearchValue(int value) => From((long)value);

    /// <summary>Implicitly converts a <see cref="long"/> to a <see cref="SearchValue"/>.</summary>
    public static implicit operator SearchValue(long value) => From(value);

    /// <summary>Implicitly converts a <see cref="double"/> to a <see cref="SearchValue"/>.</summary>
    public static implicit operator SearchValue(double value) => From(value);

    /// <summary>Implicitly converts a <see cref="bool"/> to a <see cref="SearchValue"/>.</summary>
    public static implicit operator SearchValue(bool value) => From(value);

    /// <summary>Implicitly converts a <see cref="DateTimeOffset"/> to a <see cref="SearchValue"/>.</summary>
    public static implicit operator SearchValue(DateTimeOffset value) => From(value);

    /// <summary>Implicitly converts a <see cref="Guid"/> to a <see cref="SearchValue"/>.</summary>
    public static implicit operator SearchValue(Guid value) => From(value);

    /// <summary>
    /// Returns a diagnostic string reflecting only the value actually held for <see cref="Kind"/>.
    /// </summary>
    /// <remarks>
    /// A record struct's compiler-synthesized <c>ToString()</c>/<c>PrintMembers</c> prints every
    /// public property unconditionally — including the kind-checked <see cref="AsString"/>/
    /// <see cref="AsInt64"/>/<see cref="AsDouble"/>/<see cref="AsBoolean"/>/
    /// <see cref="AsDateTimeOffset"/> accessors — which would make <c>ToString()</c> throw
    /// <see cref="InvalidOperationException"/> for every <see cref="SearchValue"/> instance (at most
    /// one accessor ever matches the held <see cref="Kind"/>). Formatting must never throw — it runs
    /// implicitly from debuggers, structured logging, and test-assertion failure messages on any
    /// containing record (<see cref="EqualFilter"/>, <see cref="RangeFilter"/>, <see cref="InFilter"/>)
    /// — so this override replaces the synthesized one entirely.
    /// </remarks>
    public override string ToString() => Kind switch
    {
        SearchValueKind.String => $"{Kind}({_stringValue})",
        SearchValueKind.Int64 => $"{Kind}({_int64Value})",
        SearchValueKind.Double => $"{Kind}({_doubleValue})",
        SearchValueKind.Boolean => $"{Kind}({_booleanValue})",
        SearchValueKind.DateTimeOffset => $"{Kind}({_dateTimeOffsetValue:O})",
        _ => $"{Kind}(?)",
    };
}
