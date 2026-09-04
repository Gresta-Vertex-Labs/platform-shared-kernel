namespace SharedKernel.Validation.NationalId;

/// <summary>
/// A per-country national-identity-number checksum validator.
/// </summary>
/// <remarks>
/// Implementations are registered against <see cref="INationalIdValidatorRegistry"/> keyed by
/// <see cref="CountryCode"/>. This platform ships <see cref="TckNationalIdValidator"/> (Turkey's
/// TCKN) as the built-in default — a consuming service adds further countries via
/// <c>AddNationalIdValidator&lt;TValidator&gt;()</c>.
/// </remarks>
public interface INationalIdValidator
{
    /// <summary>The ISO 3166-1 alpha-2 country code this validator applies to.</summary>
    string CountryCode { get; }

    /// <summary>Returns <see langword="true"/> when <paramref name="idNumber"/> passes this country's checksum algorithm.</summary>
    bool IsValid(string idNumber);
}
