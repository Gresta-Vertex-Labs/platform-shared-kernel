namespace SharedKernel.Domain;

/// <summary>
/// The stable error codes <c>SharedKernel.Domain</c> owns, in the lowercase <c>{domain}.{snake_reason}</c> form.
/// </summary>
/// <remarks>
/// Codes owned by <c>SharedKernel.Primitives</c> (<c>validation.required</c>, <c>not_found.default</c>) stay in
/// <c>ErrorCodes</c>. A business rule reports its own <see cref="BusinessRules.IBusinessRule.Code"/>, never a code
/// from this class.
/// </remarks>
public static class DomainErrorCodes
{
    /// <summary>Codes reported while creating a <see cref="Monetary.Currency"/>.</summary>
    public static class Currency
    {
        /// <summary>The currency code is not exactly three ASCII letters. Reported as a validation error.</summary>
        public const string InvalidFormat = "currency.code.invalid_format";

        /// <summary>The currency code is not in the ISO 4217 catalog. Reported as a validation error.</summary>
        public const string Unknown = "currency.code.unknown";
    }

    /// <summary>Codes reported by <see cref="Monetary.Money"/>.</summary>
    public static class Money
    {
        /// <summary>
        /// Two amounts in different currencies were combined or compared. Reported as a business-rule violation
        /// through <see cref="Exceptions.BusinessRuleViolationException"/>.
        /// </summary>
        public const string CurrencyMismatch = "money.currency_mismatch";

        /// <summary>
        /// A stored amount has more decimal places than its currency allows: corrupt stored data, thrown as a
        /// <c>DomainException</c> carrying an unexpected-type error.
        /// </summary>
        public const string StoredPrecisionExceeded = "money.stored_precision_exceeded";
    }
}
