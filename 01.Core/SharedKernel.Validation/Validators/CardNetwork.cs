namespace SharedKernel.Validation.Validators;

/// <summary>
/// The card network detected from a payment-card PAN's leading digits (BIN/IIN range).
/// </summary>
/// <remarks>
/// A plain <see langword="enum"/> rather than a <c>SmartEnum&lt;TEnum,TValue&gt;</c> — BIN-range
/// network detection carries no per-value behavior beyond the name itself, so the
/// <c>SmartEnum</c> machinery would be pure ceremony here.
/// </remarks>
public enum CardNetwork
{
    /// <summary>No known network's BIN range matched.</summary>
    Unknown = 0,

    /// <summary>Visa (leading digit 4).</summary>
    Visa,

    /// <summary>Mastercard (leading range 51-55, or the newer 2221-2720 range).</summary>
    Mastercard,

    /// <summary>American Express (leading digits 34 or 37).</summary>
    Amex,

    /// <summary>Discover (leading digits 6011, 65, 644-649, or 622126-622925).</summary>
    Discover,
}
