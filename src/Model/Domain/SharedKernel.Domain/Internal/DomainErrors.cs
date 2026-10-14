using System.Globalization;
using SharedKernel.Domain.Monetary;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Domain.Internal;

/// <summary>The single factory for the errors <see cref="DomainErrorCodes"/> names.</summary>
internal static class DomainErrors
{
    internal static Error CurrencyInvalidFormat() =>
        Error.Validation(DomainErrorCodes.Currency.InvalidFormat, "A currency code is exactly three letters.");

    internal static Error CurrencyUnknown(string code) =>
        Error.Validation(DomainErrorCodes.Currency.Unknown, $"'{code}' is not a known ISO 4217 currency code.");

    internal static Error StoredPrecisionExceeded(decimal amount, Currency currency) =>
        Error.Unexpected(
            DomainErrorCodes.Money.StoredPrecisionExceeded,
            $"Stored amount {amount.ToString(CultureInfo.InvariantCulture)} has more decimal places than " +
            $"{currency.Code}'s {currency.MinorUnitDigits}-digit minor unit allows.");
}
