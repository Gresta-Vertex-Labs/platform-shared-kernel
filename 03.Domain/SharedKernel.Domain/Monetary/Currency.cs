using SharedKernel.Domain.ValueObjects;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Domain.Monetary;

/// <summary>An ISO 4217 currency, identified by its three-letter code, with its minor-unit precision.</summary>
/// <remarks>
/// <para>
/// Only codes in <see cref="CurrencyCatalog"/> can be created, so every <see cref="Currency"/> has a known
/// <see cref="MinorUnitDigits"/>. Two currencies are equal when their codes are equal.
/// </para>
/// <para>
/// Create one with <see cref="Create"/>, which accepts any casing and surrounding whitespace, or use a
/// well-known instance such as <see cref="Usd"/>.
/// </para>
/// </remarks>
public sealed class Currency : SingleValueObject<string>
{
    private Currency(string code) : base(code) { }

    /// <summary>United States dollar.</summary>
    public static Currency Usd { get; } = new("USD");

    /// <summary>Euro.</summary>
    public static Currency Eur { get; } = new("EUR");

    /// <summary>Pound sterling.</summary>
    public static Currency Gbp { get; } = new("GBP");

    /// <summary>Japanese yen, which has no minor unit.</summary>
    public static Currency Jpy { get; } = new("JPY");

    /// <summary>Turkish lira.</summary>
    public static Currency Try { get; } = new("TRY");

    /// <summary>Gets the ISO 4217 alphabetic code, always uppercase, e.g. <c>USD</c>.</summary>
    public string Code => Value;

    /// <summary>
    /// Gets the number of decimal places of the currency's minor unit: <c>2</c> for USD, <c>0</c> for JPY,
    /// <c>3</c> for BHD.
    /// </summary>
    public int MinorUnitDigits => CurrencyCatalog.TryGetMinorUnitDigits(Code, out var digits) ? digits : 2;

    /// <summary>Creates the currency for <paramref name="code"/>.</summary>
    /// <param name="code">The ISO 4217 alphabetic code, in any casing; surrounding whitespace is ignored.</param>
    /// <returns>
    /// The currency, or a failed result with <c>currency.code.invalid_format</c> when the input is not three
    /// letters, or <c>currency.code.unknown</c> when the code is not in <see cref="CurrencyCatalog"/>.
    /// </returns>
    public static ValidationResult<Currency> Create(string? code)
    {
        var normalized = code?.Trim().ToUpperInvariant() ?? string.Empty;
        return TryCreate(() => new Currency(normalized));
    }

    /// <inheritdoc/>
    protected override IEnumerable<Error> Validate()
    {
        if (Value.Length != 3 || !Value.All(c => c is >= 'A' and <= 'Z'))
        {
            yield return Error.Validation(
                "currency.code.invalid_format",
                "A currency code is exactly three letters.");
            yield break;
        }

        if (!CurrencyCatalog.IsKnownCode(Value))
            yield return Error.Validation("currency.code.unknown", $"'{Value}' is not a known ISO 4217 currency code.");
    }
}
