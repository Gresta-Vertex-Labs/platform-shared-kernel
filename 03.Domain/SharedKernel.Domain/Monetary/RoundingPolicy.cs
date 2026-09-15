namespace SharedKernel.Domain.Monetary;

/// <summary>How an amount is rounded to its currency's minor unit.</summary>
/// <remarks>
/// The directional names avoid "up" and "down", which mean away from and toward zero in some libraries and
/// toward positive and negative infinity in others.
/// </remarks>
public enum RoundingPolicy
{
    /// <summary>
    /// Rounds to the nearest minor unit, and a midpoint to the even neighbour: 2.345 becomes 2.34, 2.355 becomes
    /// 2.36. Introduces no bias across many transactions; the default for ledgers.
    /// </summary>
    BankersRounding = 0,

    /// <summary>Rounds to the nearest minor unit, and a midpoint away from zero: 2.345 becomes 2.35, -2.345 becomes -2.35.</summary>
    AwayFromZero = 1,

    /// <summary>Discards digits beyond the minor unit: 2.349 becomes 2.34, -2.349 becomes -2.34.</summary>
    ToZero = 2,

    /// <summary>Rounds toward positive infinity: 2.341 becomes 2.35, -2.349 becomes -2.34.</summary>
    Ceiling = 3,

    /// <summary>Rounds toward negative infinity: 2.349 becomes 2.34, -2.341 becomes -2.35.</summary>
    Floor = 4,
}
