using SharedKernel.Domain.Monetary;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using ProtoMoney = Google.Type.Money;

namespace SharedKernel.Communication;

/// <summary>
/// Converts between <c>google.type.Money</c> (whole <c>units</c> plus <c>nanos</c>, billionths with the same sign) and
/// <see cref="decimal"/> or <c>SharedKernel.Domain</c>'s <see cref="Money"/>.
/// </summary>
/// <remarks>Exact within nine decimal places; a <see cref="decimal"/> with more is rounded to nine, to even.</remarks>
public static class MoneyProtoExtensions
{
    private const int NanosPerUnit = 1_000_000_000;

    /// <summary>Reads a <c>google.type.Money</c> as the <see cref="Money"/> of its currency.</summary>
    /// <param name="money">The message.</param>
    /// <returns>
    /// The amount, rounded to the currency's minor unit; or the errors of an unknown currency code or of
    /// <c>units</c>/<c>nanos</c> that break the message's rules (<c>money.proto.invalid</c>).
    /// </returns>
    public static ValidationResult<Money> ToMoney(this ProtoMoney money)
    {
        ArgumentNullException.ThrowIfNull(money);

        if (!IsValid(money))
        {
            return ValidationResult<Money>.Failure([InvalidMessage(money)]);
        }

        ValidationResult<Currency> currency = Currency.Create(money.CurrencyCode);
        return currency.IsValid
            ? Money.Create(Amount(money), currency.Value)
            : ValidationResult<Money>.Failure(currency.Errors);
    }

    /// <summary>Writes <paramref name="money"/> as a <c>google.type.Money</c>.</summary>
    /// <param name="money">The amount.</param>
    /// <returns>The message.</returns>
    public static ProtoMoney ToMoneyProto(this Money money)
    {
        ArgumentNullException.ThrowIfNull(money);
        return money.Amount.ToMoneyProto(money.Currency.Code);
    }

    /// <summary>Reads the amount of a <c>google.type.Money</c>.</summary>
    /// <param name="money">The message.</param>
    /// <returns>The amount.</returns>
    /// <exception cref="ArgumentException"><c>nanos</c> is out of range or its sign differs from <c>units</c>.</exception>
    public static decimal ToDecimal(this ProtoMoney money)
    {
        ArgumentNullException.ThrowIfNull(money);
        return IsValid(money)
            ? Amount(money)
            : throw new ArgumentException(InvalidMessage(money).Message, nameof(money));
    }

    /// <summary>Writes an amount as a <c>google.type.Money</c> in <paramref name="currencyCode"/>.</summary>
    /// <param name="amount">The amount; rounded to nine decimal places.</param>
    /// <param name="currencyCode">The ISO 4217 code, such as <c>EUR</c>.</param>
    /// <returns>The message.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The whole part does not fit <c>units</c> (a 64-bit integer).</exception>
    public static ProtoMoney ToMoneyProto(this decimal amount, string currencyCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currencyCode);

        // Round first, then split: the fraction of a nine-place decimal times 10^9 is an exact integer below 10^9.
        decimal rounded = decimal.Round(amount, 9, MidpointRounding.ToEven);
        decimal whole = decimal.Truncate(rounded);
        if (whole is > long.MaxValue or < long.MinValue)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "The whole part does not fit google.type.Money's 64-bit units.");
        }

        return new ProtoMoney
        {
            Units = (long)whole,
            Nanos = (int)((rounded - whole) * NanosPerUnit),
            CurrencyCode = currencyCode.Trim().ToUpperInvariant(),
        };
    }

    private static decimal Amount(ProtoMoney money) => money.Units + ((decimal)money.Nanos / NanosPerUnit);

    private static bool IsValid(ProtoMoney money) =>
        money.Nanos is > -NanosPerUnit and < NanosPerUnit
        && !(money.Units > 0 && money.Nanos < 0)
        && !(money.Units < 0 && money.Nanos > 0);

    private static Error InvalidMessage(ProtoMoney money) => Error.Validation(
        "money.proto.invalid",
        $"google.type.Money with units {money.Units} and nanos {money.Nanos} is invalid: nanos must be within ±999,999,999 and share the sign of units.");
}
