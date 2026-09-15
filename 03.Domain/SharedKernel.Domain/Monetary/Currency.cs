using SharedKernel.Domain.ValueObjects;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Domain.Monetary;

/// <summary>
/// An ISO 4217 currency: a three-letter code from <see cref="CurrencyCatalog"/> and the number of decimal places
/// of its minor unit.
/// </summary>
/// <remarks>
/// <para>
/// <b>Validation.</b> Only codes in <see cref="CurrencyCatalog"/> can be created, so every instance has a known
/// <see cref="MinorUnitDigits"/>. Create one with <see cref="Create"/>, which ignores casing and surrounding
/// whitespace, or use a well-known instance such as <see cref="Usd"/>.
/// </para>
/// <para>
/// <b>Equality.</b> Two currencies are equal when their uppercase codes are equal. <see cref="object.ToString"/>
/// returns the code.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var result = Currency.Create(" try ");
/// if (result.IsValid)
///     Console.WriteLine(result.Value.MinorUnitDigits); // 2
/// </code>
/// </example>
public sealed class Currency : SingleValueObject<string>
{
    private Currency(string code) : base(code) { }

    /// <summary>Gets the United States dollar (<c>USD</c>, 2 minor-unit digits).</summary>
    public static Currency Usd { get; } = new("USD");

    /// <summary>Gets the euro (<c>EUR</c>, 2 minor-unit digits).</summary>
    public static Currency Eur { get; } = new("EUR");

    /// <summary>Gets the pound sterling (<c>GBP</c>, 2 minor-unit digits).</summary>
    public static Currency Gbp { get; } = new("GBP");

    /// <summary>Gets the Japanese yen (<c>JPY</c>), which has no minor unit (0 digits).</summary>
    public static Currency Jpy { get; } = new("JPY");

    /// <summary>Gets the Turkish lira (<c>TRY</c>, 2 minor-unit digits).</summary>
    public static Currency Try { get; } = new("TRY");

    /// <summary>Gets the ISO 4217 alphabetic code, always three uppercase ASCII letters, e.g. <c>USD</c>.</summary>
    public string Code => Value;

    /// <summary>
    /// Gets the number of decimal places of the currency's minor unit: <c>2</c> for USD, <c>0</c> for JPY,
    /// <c>3</c> for BHD.
    /// </summary>
    public int MinorUnitDigits => CurrencyCatalog.TryGetMinorUnitDigits(Code, out var digits) ? digits : 2;

    /// <summary>Creates the currency for <paramref name="code"/>.</summary>
    /// <param name="code">
    /// The ISO 4217 alphabetic code, in any casing; surrounding whitespace is ignored. <see langword="null"/> is
    /// treated as empty.
    /// </param>
    /// <returns>
    /// A successful result holding the currency; or a failed result with the single error
    /// <c>currency.code.invalid_format</c> when the trimmed input is not exactly three ASCII letters, or
    /// <c>currency.code.unknown</c> when the code is not in <see cref="CurrencyCatalog"/>. Never throws for any
    /// input.
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
