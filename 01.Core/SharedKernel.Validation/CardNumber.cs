using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Validation.Internal;

namespace SharedKernel.Validation;

/// <summary>
/// A payment card number (PAN): 12 to 19 digits that pass the Luhn check, with the card network
/// detected from the leading digits.
/// </summary>
/// <remarks>
/// <para>
/// <b><see cref="ToString"/> is masked.</b> It returns the first six and last four digits with
/// the rest replaced by <c>*</c>, the most PCI DSS allows to display. A card number that reaches
/// a log through string interpolation or a structured-logging argument is therefore masked
/// automatically. <see cref="Value"/> holds the full number: read it only where the full number is
/// meant to go, such as the call to your payment provider.
/// </para>
/// <para>
/// Spaces and hyphens are removed. Validating a card number says nothing about whether the card
/// exists or has funds.
/// </para>
/// </remarks>
[JsonConverter(typeof(ValidatedValueJsonConverter<CardNumber>))]
[DebuggerDisplay("{Masked,nq}")]
public readonly record struct CardNumber : IValidatedValue<CardNumber>
{
    private readonly string? _value;

    private CardNumber(string value, CardNetwork network)
    {
        _value = value;
        Network = network;
    }

    /// <summary>Gets the full card number, digits only. Handle it as cardholder data.</summary>
    public string Value => _value ?? string.Empty;

    /// <summary>Gets the detected card network, or <see cref="CardNetwork.Unknown"/>.</summary>
    public CardNetwork Network { get; }

    /// <summary>Gets the first six digits, the issuer identification number.</summary>
    public string Iin => Value.Length >= 6 ? Value[..6] : string.Empty;

    /// <summary>Gets the last four digits.</summary>
    public string Last4 => Value.Length >= 4 ? Value[^4..] : string.Empty;

    /// <summary>Gets the number with all but the first six and last four digits replaced by <c>*</c>.</summary>
    public string Masked => Value.Length < 12 ? string.Empty : Iin + new string('*', Value.Length - 10) + Last4;

    /// <summary>Validates and normalizes <paramref name="value"/>. A card from an unknown network is accepted.</summary>
    /// <param name="value">The card number, with or without spaces and hyphens.</param>
    /// <returns>The card number, or the reason it is invalid.</returns>
    public static Result<CardNumber> Create(string? value) => Create(value, requireKnownNetwork: false);

    /// <summary>Validates and normalizes <paramref name="value"/>.</summary>
    /// <param name="value">The card number, with or without spaces and hyphens.</param>
    /// <param name="requireKnownNetwork">
    /// When <see langword="true"/>, a number that passes the Luhn check but matches no known network
    /// fails with <see cref="ValidationErrorCodes.CardNumber.UnknownNetwork"/>.
    /// </param>
    /// <returns>The card number, or the reason it is invalid.</returns>
    public static Result<CardNumber> Create(string? value, bool requireKnownNetwork)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ValidationMessages.Required.ToError(ErrorType.Validation);
        }

        string digits = Text.Compact(value, " -");
        if (digits.Length is < 12 or > 19 || !Text.AllDigits(digits))
        {
            return ValidationMessages.CardNumberInvalidFormat.ToError(ErrorType.Validation);
        }

        if (Checksums.Luhn(digits) != 0)
        {
            return ValidationMessages.CardNumberInvalidCheckDigit.ToError(ErrorType.Validation);
        }

        CardNetwork network = CardNetworkTable.Detect(digits);
        return requireKnownNetwork && network == CardNetwork.Unknown
            ? ValidationMessages.CardNumberUnknownNetwork.ToError(ErrorType.Validation)
            : new CardNumber(digits, network);
    }

    /// <summary>Returns whether <paramref name="value"/> is a valid card number.</summary>
    /// <param name="value">The card number to check.</param>
    /// <returns><see langword="true"/> when <see cref="Create(string?)"/> would succeed.</returns>
    public static bool IsValid([NotNullWhen(true)] string? value) => Create(value).IsSuccess;

    /// <inheritdoc />
    public static CardNumber Parse(string s, IFormatProvider? provider) => ValueParsing.Parse<CardNumber>(s);

    /// <inheritdoc />
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out CardNumber result) =>
        ValueParsing.TryParse(s, out result);

    /// <summary>Returns <see cref="Masked"/>, never the full number.</summary>
    /// <returns>The masked card number.</returns>
    public override string ToString() => Masked;
}
