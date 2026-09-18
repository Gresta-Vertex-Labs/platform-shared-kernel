namespace SharedKernel.Validation;

/// <summary>
/// The card network a <see cref="CardNumber"/> belongs to, detected from its leading digits (the
/// issuer identification number) and its length.
/// </summary>
/// <remarks>
/// Detection uses published issuer ranges and is best effort: networks add ranges, and co-branded
/// cards belong to two networks. When the network decides something that matters, such as which
/// acquirer to route to, confirm it with your payment provider's BIN lookup.
/// </remarks>
public enum CardNetwork
{
    /// <summary>No known network matches the leading digits and length.</summary>
    Unknown = 0,

    /// <summary>Visa: starts with 4; 13, 16 or 19 digits.</summary>
    Visa = 1,

    /// <summary>Mastercard: 51–55 or 2221–2720; 16 digits.</summary>
    Mastercard = 2,

    /// <summary>American Express: 34 or 37; 15 digits.</summary>
    AmericanExpress = 3,

    /// <summary>Discover: 6011, 644–649 or 65 (including Troy cards co-branded with Discover); 16–19 digits.</summary>
    Discover = 4,

    /// <summary>JCB: 3528–3589; 16–19 digits.</summary>
    Jcb = 5,

    /// <summary>UnionPay: 62; 16–19 digits.</summary>
    UnionPay = 6,

    /// <summary>Diners Club International: 30, 36, 38 or 39; 14–19 digits.</summary>
    DinersClub = 7,

    /// <summary>Maestro: 5018, 5020, 5038, 5893, 6304, 6759, 6761–6763; 12–19 digits.</summary>
    Maestro = 8,

    /// <summary>Mir (Russia): 2200–2204; 16–19 digits.</summary>
    Mir = 9,

    /// <summary>Troy (Türkiye's domestic network): 9792; 16 digits.</summary>
    Troy = 10,
}
