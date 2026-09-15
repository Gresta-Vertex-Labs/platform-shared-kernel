using SharedKernel.Domain.BusinessRules;

namespace SharedKernel.Domain.Monetary;

/// <summary>
/// A business rule that is broken when a currency differs from the expected one, with the stable error code
/// <c>money.currency_mismatch</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Usage.</b> <see cref="Money"/> checks this rule on every operation that combines or orders two amounts and
/// throws <see cref="SharedKernel.Domain.Exceptions.BusinessRuleViolationException"/> when it is broken. Check it
/// directly to reject a mismatched currency before any arithmetic, for example when an incoming payment must
/// match an invoice.
/// </para>
/// <para>
/// <b>Equality.</b> Currencies are compared by code, so the rule is broken exactly when the two codes differ.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// CheckRule(new CurrencyMismatchRule(expected: Total.Currency, actual: payment.Currency));
/// </code>
/// </example>
public sealed class CurrencyMismatchRule : IBusinessRule
{
    /// <summary>The stable error code reported when the rule is broken: <c>money.currency_mismatch</c>.</summary>
    public const string ErrorCode = "money.currency_mismatch";

    private readonly Currency _expected;
    private readonly Currency _actual;

    /// <summary>
    /// Initializes a new rule that is broken when <paramref name="actual"/> differs from <paramref name="expected"/>.
    /// </summary>
    /// <param name="expected">The required currency. Must not be <see langword="null"/>.</param>
    /// <param name="actual">The currency found. Must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="expected"/> or <paramref name="actual"/> is <see langword="null"/>.
    /// </exception>
    public CurrencyMismatchRule(Currency expected, Currency actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        _expected = expected;
        _actual = actual;
    }

    /// <summary>Gets the stable error code, always <see cref="ErrorCode"/> (<c>money.currency_mismatch</c>).</summary>
    public string Code => ErrorCode;

    /// <summary>Gets the message naming both codes, e.g. <c>Expected an amount in USD but got EUR.</c></summary>
    public string Message => $"Expected an amount in {_expected.Code} but got {_actual.Code}.";

    /// <summary>Returns whether the actual currency differs from the expected one.</summary>
    /// <returns><see langword="true"/> when the currency codes differ; otherwise <see langword="false"/>.</returns>
    public bool IsBroken() => !_expected.Equals(_actual);
}
