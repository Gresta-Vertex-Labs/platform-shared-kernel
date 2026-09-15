using SharedKernel.Domain.BusinessRules;

namespace SharedKernel.Domain.Monetary;

/// <summary>A rule broken when two currencies differ, which is why amounts in different currencies cannot be combined.</summary>
/// <remarks>
/// <see cref="Money"/> enforces it on every operation with two amounts. Use it directly to reject a mismatched
/// currency before constructing money, for example when an incoming payment must match an invoice.
/// </remarks>
public sealed class CurrencyMismatchRule : IBusinessRule
{
    /// <summary>The code reported when the rule is broken.</summary>
    public const string ErrorCode = "money.currency_mismatch";

    private readonly Currency _expected;
    private readonly Currency _actual;

    /// <summary>Creates the rule comparing <paramref name="actual"/> with <paramref name="expected"/>.</summary>
    /// <param name="expected">The required currency.</param>
    /// <param name="actual">The currency found.</param>
    /// <exception cref="ArgumentNullException"><paramref name="expected"/> or <paramref name="actual"/> is <see langword="null"/>.</exception>
    public CurrencyMismatchRule(Currency expected, Currency actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        _expected = expected;
        _actual = actual;
    }

    /// <inheritdoc/>
    public string Code => ErrorCode;

    /// <inheritdoc/>
    public string Message => $"Expected an amount in {_expected.Code} but got {_actual.Code}.";

    /// <inheritdoc/>
    public bool IsBroken() => !_expected.Equals(_actual);
}
