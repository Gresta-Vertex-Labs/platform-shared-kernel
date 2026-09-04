namespace SharedKernel.Validation.NationalId;

/// <summary>
/// A pluggable, per-country registry of <see cref="INationalIdValidator"/> implementations.
/// </summary>
public interface INationalIdValidatorRegistry
{
    /// <summary>
    /// Attempts to resolve the <see cref="INationalIdValidator"/> registered for
    /// <paramref name="countryCode"/> (ISO 3166-1 alpha-2, case-insensitive).
    /// </summary>
    /// <param name="countryCode">The ISO 3166-1 alpha-2 country code to look up.</param>
    /// <param name="validator">The resolved validator, or <see langword="null"/> if none is registered.</param>
    /// <returns><see langword="true"/> when a validator was found; <see langword="false"/> otherwise — this method never throws.</returns>
    bool TryGetValidator(string countryCode, out INationalIdValidator? validator);
}
