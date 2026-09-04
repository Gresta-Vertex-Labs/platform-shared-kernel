namespace SharedKernel.Domain.ValueObjects.Money;

/// <summary>
/// Selects how a <see cref="Money"/> amount is rounded to its <see cref="Currency"/>'s
/// ISO 4217 minor-unit precision.
/// </summary>
/// <remarks>
/// WO-066/P-439. Rounding is applied unconditionally at <see cref="Money"/> construction —
/// there is no "reject excess precision" validation path. See <see cref="Money.Create"/>.
/// </remarks>
public enum RoundingPolicy
{
    /// <summary>
    /// Default. Maps to <see cref="MidpointRounding.ToEven"/> ("banker's rounding") — introduces
    /// no systematic bias across a large volume of transactions, the standard choice for
    /// financial ledgers.
    /// </summary>
    BankersRounding = 0,

    /// <summary>
    /// Maps to <see cref="MidpointRounding.AwayFromZero"/>. Explicit opt-in for
    /// jurisdictions/contracts that require it.
    /// </summary>
    AwayFromZero = 1,
}
