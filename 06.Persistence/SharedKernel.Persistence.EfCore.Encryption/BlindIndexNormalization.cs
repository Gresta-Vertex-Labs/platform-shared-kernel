namespace SharedKernel.Persistence.EfCore.Encryption;

/// <summary>
/// Built-in normalization applied to a value before its blind index is computed, so values that should compare
/// equal produce the same index.
/// </summary>
/// <remarks>
/// Steps run in a fixed order: <see cref="Trim"/>, then <see cref="RemoveWhitespace"/>, then <see cref="CaseFold"/>,
/// then a named <see cref="BlindIndex.IBlindIndexNormalizer"/> if the property declares one. Changing a property's
/// normalization changes every index it produces: run the maintenance job with
/// <c>EncryptionMaintenanceMode.RecomputeBlindIndexes</c> afterwards.
/// </remarks>
[Flags]
public enum BlindIndexNormalization
{
    /// <summary>The value is indexed exactly as stored.</summary>
    None = 0,

    /// <summary>Leading and trailing white space is removed.</summary>
    Trim = 1,

    /// <summary>The value is lower-cased with the invariant culture.</summary>
    CaseFold = 2,

    /// <summary>Every white-space character is removed (for example spaces inside an IBAN).</summary>
    RemoveWhitespace = 4,
}
