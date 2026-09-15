namespace SharedKernel.Domain.Monetary;

/// <summary>
/// The rounding policy that decides how an amount with more decimal places than its currency's minor unit is
/// rounded to that minor unit.
/// </summary>
/// <remarks>
/// <para>
/// <b>Where it applies.</b> <see cref="Money"/> rounds only when a value is created: <see cref="Money.Create"/>,
/// <see cref="Money.Multiply"/>, <see cref="Money.Divide"/> and <see cref="MoneyExtensions.ConvertAsync"/> take a
/// policy, and the <c>*</c> and <c>/</c> operators use <see cref="BankersRounding"/>. The policy is not stored on
/// the amount. The number of digits comes from <see cref="Currency.MinorUnitDigits"/>, so the examples below,
/// written for a two-digit currency, round to whole units for <c>JPY</c>.
/// </para>
/// <para>
/// <b>Naming.</b> The directional names avoid "up" and "down", which mean away from and toward zero in some
/// libraries and toward positive and negative infinity in others.
/// </para>
/// <para>
/// <b>Validation.</b> A value outside the defined members makes <see cref="Money.Create"/> return a failed result,
/// and makes <see cref="Money.Multiply"/>, <see cref="Money.Divide"/> and
/// <see cref="MoneyExtensions.ConvertAsync"/> throw <c>DomainException</c>.
/// </para>
/// </remarks>
public enum RoundingPolicy
{
    /// <summary>
    /// Rounds to the nearest minor unit, and a midpoint to the even neighbour: 2.345 becomes 2.34, 2.355 becomes
    /// 2.36, -2.345 becomes -2.34. Introduces no systematic bias across many transactions; the default everywhere.
    /// </summary>
    BankersRounding = 0,

    /// <summary>
    /// Rounds to the nearest minor unit, and a midpoint away from zero: 2.345 becomes 2.35, -2.345 becomes -2.35.
    /// </summary>
    AwayFromZero = 1,

    /// <summary>
    /// Rounds toward zero, discarding digits beyond the minor unit: 2.349 becomes 2.34, -2.349 becomes -2.34.
    /// </summary>
    ToZero = 2,

    /// <summary>Rounds toward positive infinity: 2.341 becomes 2.35, -2.349 becomes -2.34.</summary>
    Ceiling = 3,

    /// <summary>Rounds toward negative infinity: 2.349 becomes 2.34, -2.341 becomes -2.35.</summary>
    Floor = 4,
}
