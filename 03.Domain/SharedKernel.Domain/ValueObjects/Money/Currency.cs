using SharedKernel.Domain.ValueObjects;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Domain.ValueObjects.Money;

/// <summary>
/// A validated ISO 4217 currency identity — a three-letter alpha code with a known, catalog-backed
/// minor-unit (decimal-place) precision.
/// </summary>
/// <remarks>
/// WO-066/P-439. Extends <see cref="SingleValueObject{TValue}"/> of <see cref="string"/> —
/// equality and hashing are structural over <see cref="SingleValueObject{TValue}.Value"/>
/// (= <see cref="Code"/>). Construct via <see cref="Create"/>, or use one of the well-known
/// static convenience instances (<see cref="Usd"/>, <see cref="Eur"/>, <see cref="Gbp"/>,
/// <see cref="Jpy"/>) — a DX convenience only, not an exhaustive currency list.
/// </remarks>
public sealed class Currency : SingleValueObject<string>
{
    private Currency(string code) : base(code) { }

    /// <summary>Gets the ISO 4217 alpha-3 currency code (always uppercase).</summary>
    public string Code => Value;

    /// <summary>
    /// Gets the number of decimal places this currency's minor unit carries (e.g. <c>2</c> for
    /// USD, <c>0</c> for JPY, <c>3</c> for BHD), resolved from <see cref="CurrencyCatalog"/>.
    /// </summary>
    /// <remarks>
    /// Derived, not an equality component — two <see cref="Currency"/> instances are equal
    /// purely by <see cref="Code"/>. Every successfully constructed <see cref="Currency"/> is
    /// already a known catalog code (see <see cref="Validate"/>), so the catalog lookup here
    /// always succeeds; the <c>2</c> fallback exists only as a defensive default.
    /// </remarks>
    public int MinorUnitDigits =>
        CurrencyCatalog.TryGetMinorUnitDigits(Code, out var digits) ? digits : 2;

    /// <inheritdoc/>
    protected override IEnumerable<Error>? Validate()
    {
        if (Value.Length != 3 || !IsUppercaseAsciiLetters(Value))
        {
            yield return Error.Validation(
                "currency.code.invalid_format",
                "Currency code must be exactly 3 uppercase ASCII letters.");
            yield break;
        }

        if (!CurrencyCatalog.IsKnownCode(Value))
            yield return Error.Validation(
                "currency.code.unknown",
                $"'{Value}' is not a recognized ISO 4217 currency code.");
    }

    /// <summary>
    /// Creates a <see cref="Currency"/> from <paramref name="code"/>, normalizing casing and
    /// surrounding whitespace before validating against <see cref="CurrencyCatalog"/>.
    /// </summary>
    /// <param name="code">The ISO 4217 alpha-3 currency code, in any casing.</param>
    public static Result<Currency> Create(string code)
    {
        var normalized = code?.Trim().ToUpperInvariant() ?? string.Empty;
        return TryCreate(() => new Currency(normalized));
    }

    private static bool IsUppercaseAsciiLetters(string value)
    {
        foreach (var c in value)
        {
            if (c is < 'A' or > 'Z')
                return false;
        }

        return true;
    }

    /// <summary>United States Dollar.</summary>
    public static readonly Currency Usd = new("USD");

    /// <summary>Euro.</summary>
    public static readonly Currency Eur = new("EUR");

    /// <summary>British Pound Sterling.</summary>
    public static readonly Currency Gbp = new("GBP");

    /// <summary>Japanese Yen (zero-decimal).</summary>
    public static readonly Currency Jpy = new("JPY");
}
