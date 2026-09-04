using SharedKernel.Domain.ValueObjects.Money;

namespace SharedKernel.Domain.BusinessRules;

/// <summary>
/// A business rule that is broken when two <see cref="Currency"/> values are not equal —
/// the invariant behind every cross-currency-rejecting <see cref="Money"/> operation.
/// </summary>
/// <remarks>
/// WO-066/P-439. Public, top-level, and reusable — a sibling to <see cref="AndBusinessRule"/>/
/// <see cref="OrBusinessRule"/>/<see cref="NotBusinessRule"/>, not nested inside <see cref="Money"/>
/// — so a consuming service can enforce currency agreement independently of <see cref="Money"/>
/// construction (e.g. validating an incoming payment DTO's currency before ever constructing a
/// <see cref="Money"/> instance). <see cref="Money"/>'s cross-currency arithmetic calls
/// <c>ValueObject.CheckRule(new CurrencyMismatchRule(...))</c>, reusing the already-shipped
/// <see cref="SharedKernel.Domain.Exceptions.BusinessRuleViolationException"/> / <c>ErrorType.BusinessRule</c> →
/// HTTP 422 pipeline — no new exception type was introduced for this rule.
/// </remarks>
public sealed class CurrencyMismatchRule : IBusinessRule
{
    private readonly Currency _expected;
    private readonly Currency _actual;

    /// <summary>
    /// Initialises a new <see cref="CurrencyMismatchRule"/> comparing <paramref name="expected"/>
    /// against <paramref name="actual"/>.
    /// </summary>
    /// <param name="expected">The currency the operation requires.</param>
    /// <param name="actual">The currency actually supplied.</param>
    public CurrencyMismatchRule(Currency expected, Currency actual)
    {
        _expected = expected;
        _actual = actual;
    }

    /// <inheritdoc/>
    public string Message => $"Currency mismatch: expected '{_expected.Code}' but was '{_actual.Code}'.";

    /// <inheritdoc/>
    /// <remarks>Broken when <c>expected</c> and <c>actual</c> are not equal by value (not reference).</remarks>
    public bool IsBroken() => !_expected.Equals(_actual);
}
