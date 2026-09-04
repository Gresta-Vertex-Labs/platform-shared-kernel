namespace SharedKernel.DataPrivacy.Classification;

/// <summary>
/// Broad sensitivity tiers for classifying a piece of data, from freely shareable to maximally
/// restricted.
/// </summary>
/// <remarks>
/// Applied to a property or field via <see cref="DataClassificationAttribute"/>. See that
/// attribute's remarks for the platform's rule on how (and how not) this classification may be
/// consumed.
/// </remarks>
public enum DataClassification
{
    /// <summary>Freely shareable data with no confidentiality concerns.</summary>
    Public,

    /// <summary>
    /// Internal-use data not intended for external disclosure, but not independently sensitive
    /// enough to warrant <see cref="Confidential"/> or <see cref="Restricted"/> handling.
    /// </summary>
    Internal,

    /// <summary>Sensitive data requiring controlled access and handling.</summary>
    Confidential,

    /// <summary>
    /// The platform's most sensitive tier — requires the strictest access controls and handling
    /// (e.g. credentials, payment card numbers, health records).
    /// </summary>
    Restricted,
}
