using System.Globalization;

namespace SharedKernel.AI.Abstractions.Models;

/// <summary>
/// A closed, five-kind scalar union used as the only value type <see cref="VectorFilter"/> nodes and
/// <see cref="IVectorRecord.Metadata"/> entries may carry — the direct antidote to typing a metadata
/// value as <c>object</c> or <c>dynamic</c>.
/// </summary>
/// <remarks>
/// <para>
/// Construct via the <see cref="From(string)"/> family of static factories or the implicit conversion
/// operators — there is no public constructor. Read back via the kind-checked
/// <see cref="AsString"/>/<see cref="AsInt64"/>/<see cref="AsDouble"/>/<see cref="AsBoolean"/>/
/// <see cref="AsDateTimeOffset"/> accessors; reading the wrong accessor for the current
/// <see cref="Kind"/> throws <see cref="InvalidOperationException"/> — an adapter programming error,
/// never a consumer-facing expected failure.
/// </para>
/// <para>
/// <see cref="From(Guid)"/> normalises to the canonical <c>"D"</c> string form (lowercase, hyphenated,
/// no braces) so a <see cref="Guid"/> filter value is identical on both vector providers.
/// </para>
/// <para>
/// <b>Deliberate structural twin of <c>09.Search</c>'s <c>SearchValue</c>:</b> same five kinds, same
/// accessor shape, same <see cref="From(Guid)"/> canonical-<c>"D"</c>-string normalisation. Both
/// Qdrant payload values and Milvus scalar fields are faithfully representable by exactly these five
/// kinds — no engine asymmetry motivates a different union here.
/// </para>
/// <para>
/// <b>Timestamp encoding is a provider concern, not modelled here:</b> <see cref="DateTimeOffset"/>
/// values are serialised to whatever each provider's own numeric/string convention requires (Qdrant
/// payload: RFC 3339 string; Milvus: epoch-microseconds <see cref="long"/>) — decided and implemented
/// per-provider at Core phase.
/// </para>
/// </remarks>
public readonly record struct VectorValue
{
    private readonly string? _stringValue;
    private readonly long _int64Value;
    private readonly double _doubleValue;
    private readonly bool _booleanValue;
    private readonly DateTimeOffset _dateTimeOffsetValue;

    private VectorValue(
        VectorValueKind kind,
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
    public VectorValueKind Kind { get; }

    /// <summary>Gets the held value as a <see cref="string"/>.</summary>
    /// <exception cref="InvalidOperationException"><see cref="Kind"/> is not <see cref="VectorValueKind.String"/>.</exception>
    public string AsString => Kind == VectorValueKind.String
        ? _stringValue!
        : throw new InvalidOperationException($"VectorValue holds {Kind}, not {VectorValueKind.String}.");

    /// <summary>Gets the held value as an <see cref="long"/>.</summary>
    /// <exception cref="InvalidOperationException"><see cref="Kind"/> is not <see cref="VectorValueKind.Int64"/>.</exception>
    public long AsInt64 => Kind == VectorValueKind.Int64
        ? _int64Value
        : throw new InvalidOperationException($"VectorValue holds {Kind}, not {VectorValueKind.Int64}.");

    /// <summary>Gets the held value as a <see cref="double"/>.</summary>
    /// <exception cref="InvalidOperationException"><see cref="Kind"/> is not <see cref="VectorValueKind.Double"/>.</exception>
    public double AsDouble => Kind == VectorValueKind.Double
        ? _doubleValue
        : throw new InvalidOperationException($"VectorValue holds {Kind}, not {VectorValueKind.Double}.");

    /// <summary>Gets the held value as a <see cref="bool"/>.</summary>
    /// <exception cref="InvalidOperationException"><see cref="Kind"/> is not <see cref="VectorValueKind.Boolean"/>.</exception>
    public bool AsBoolean => Kind == VectorValueKind.Boolean
        ? _booleanValue
        : throw new InvalidOperationException($"VectorValue holds {Kind}, not {VectorValueKind.Boolean}.");

    /// <summary>Gets the held value as a <see cref="DateTimeOffset"/>.</summary>
    /// <exception cref="InvalidOperationException"><see cref="Kind"/> is not <see cref="VectorValueKind.DateTimeOffset"/>.</exception>
    public DateTimeOffset AsDateTimeOffset => Kind == VectorValueKind.DateTimeOffset
        ? _dateTimeOffsetValue
        : throw new InvalidOperationException($"VectorValue holds {Kind}, not {VectorValueKind.DateTimeOffset}.");

    /// <summary>Creates a <see cref="VectorValueKind.String"/> value.</summary>
    public static VectorValue From(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new VectorValue(VectorValueKind.String, stringValue: value);
    }

    /// <summary>Creates a <see cref="VectorValueKind.Int64"/> value.</summary>
    public static VectorValue From(long value) => new(VectorValueKind.Int64, int64Value: value);

    /// <summary>Creates a <see cref="VectorValueKind.Double"/> value.</summary>
    public static VectorValue From(double value) => new(VectorValueKind.Double, doubleValue: value);

    /// <summary>Creates a <see cref="VectorValueKind.Boolean"/> value.</summary>
    public static VectorValue From(bool value) => new(VectorValueKind.Boolean, booleanValue: value);

    /// <summary>Creates a <see cref="VectorValueKind.DateTimeOffset"/> value.</summary>
    public static VectorValue From(DateTimeOffset value) => new(VectorValueKind.DateTimeOffset, dateTimeOffsetValue: value);

    /// <summary>
    /// Creates a <see cref="VectorValueKind.String"/> value from a <see cref="Guid"/>, normalised to
    /// its canonical <c>"D"</c> string form (lowercase, hyphenated, no braces) — identical on both
    /// providers.
    /// </summary>
    public static VectorValue From(Guid value) => From(value.ToString("D", CultureInfo.InvariantCulture));

    /// <summary>Implicitly converts a <see cref="string"/> to a <see cref="VectorValue"/>.</summary>
    public static implicit operator VectorValue(string value) => From(value);

    /// <summary>Implicitly converts an <see cref="int"/> to a <see cref="VectorValue"/>.</summary>
    public static implicit operator VectorValue(int value) => From((long)value);

    /// <summary>Implicitly converts a <see cref="long"/> to a <see cref="VectorValue"/>.</summary>
    public static implicit operator VectorValue(long value) => From(value);

    /// <summary>Implicitly converts a <see cref="double"/> to a <see cref="VectorValue"/>.</summary>
    public static implicit operator VectorValue(double value) => From(value);

    /// <summary>Implicitly converts a <see cref="bool"/> to a <see cref="VectorValue"/>.</summary>
    public static implicit operator VectorValue(bool value) => From(value);

    /// <summary>Implicitly converts a <see cref="DateTimeOffset"/> to a <see cref="VectorValue"/>.</summary>
    public static implicit operator VectorValue(DateTimeOffset value) => From(value);

    /// <summary>Implicitly converts a <see cref="Guid"/> to a <see cref="VectorValue"/>.</summary>
    public static implicit operator VectorValue(Guid value) => From(value);

    /// <summary>
    /// Returns a diagnostic string reflecting only the value actually held for <see cref="Kind"/>.
    /// </summary>
    /// <remarks>
    /// A record struct's compiler-synthesized <c>ToString()</c>/<c>PrintMembers</c> prints every
    /// public property unconditionally — including the kind-checked <see cref="AsString"/>/
    /// <see cref="AsInt64"/>/<see cref="AsDouble"/>/<see cref="AsBoolean"/>/
    /// <see cref="AsDateTimeOffset"/> accessors — which would make the compiler-synthesized
    /// <c>ToString()</c> throw <see cref="InvalidOperationException"/> for every instance (at most one
    /// accessor ever matches the held <see cref="Kind"/>). Formatting must never throw — it runs
    /// implicitly from debuggers, structured logging, and test-assertion failure messages on any
    /// containing record — so this override replaces the synthesized one entirely, avoided by design
    /// from day one rather than discovered and fixed later (the <c>09.Search</c> <c>SearchValue</c>
    /// defect precedent).
    /// </remarks>
    public override string ToString() => Kind switch
    {
        VectorValueKind.String => $"{Kind}({_stringValue})",
        VectorValueKind.Int64 => $"{Kind}({_int64Value})",
        VectorValueKind.Double => $"{Kind}({_doubleValue})",
        VectorValueKind.Boolean => $"{Kind}({_booleanValue})",
        VectorValueKind.DateTimeOffset => $"{Kind}({_dateTimeOffsetValue:O})",
        _ => $"{Kind}(?)",
    };
}
