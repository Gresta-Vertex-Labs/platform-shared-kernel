namespace SharedKernel.DataPrivacy.Classification;

/// <summary>
/// Well-known categories of sensitive data, orthogonal to <see cref="DataClassification"/>'s
/// broad sensitivity tiers — this enum answers "what kind of sensitive data is this", not "how
/// sensitive is it".
/// </summary>
/// <remarks>
/// Applied to a property or field via <see cref="SensitiveDataCategoryAttribute"/>. See that
/// attribute's remarks for the platform's rule on how (and how not) this category may be consumed.
/// </remarks>
public enum SensitiveDataCategory
{
    /// <summary>Personally identifiable information (name, email, phone number, address, national ID, etc.).</summary>
    Pii,

    /// <summary>Payment card data (PAN, CVV, expiry, cardholder name).</summary>
    PaymentCard,

    /// <summary>A credential or secret (password, API key, recovery code, token).</summary>
    Credential,

    /// <summary>Health-related data (diagnoses, treatment history, medical record identifiers).</summary>
    Health,
}
