using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;

namespace SharedKernel.Validation;

/// <summary>
/// The national ID validators available to <see cref="NationalId"/>, one per country. Always
/// includes <see cref="TurkishNationalIdValidator"/>; a validator you pass for the same country
/// replaces it.
/// </summary>
/// <remarks>
/// Immutable and thread-safe. <see cref="ValidationServiceCollectionExtensions.AddSharedKernelValidation"/>
/// registers one built from every <see cref="INationalIdValidator"/> in the container.
/// </remarks>
public sealed class NationalIdValidatorRegistry
{
    private readonly FrozenDictionary<string, INationalIdValidator> _validators;

    /// <summary>Creates a registry with the built-in validators plus <paramref name="validators"/>.</summary>
    /// <param name="validators">Additional validators; a later one replaces an earlier one for the same country.</param>
    /// <exception cref="ArgumentNullException"><paramref name="validators"/> or one of its items is null.</exception>
    public NationalIdValidatorRegistry(IEnumerable<INationalIdValidator> validators)
    {
        ArgumentNullException.ThrowIfNull(validators);

        var all = new Dictionary<string, INationalIdValidator>(StringComparer.Ordinal);
        foreach (INationalIdValidator validator in validators.Prepend(new TurkishNationalIdValidator()))
        {
            ArgumentNullException.ThrowIfNull(validator, nameof(validators));
            all[validator.Country.Value] = validator;
        }

        _validators = all.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>Gets a registry with only the built-in validators.</summary>
    public static NationalIdValidatorRegistry Default { get; } = new([]);

    /// <summary>Gets the countries that have a validator.</summary>
    public IReadOnlyCollection<string> Countries => _validators.Keys;

    /// <summary>Finds the validator for <paramref name="country"/>.</summary>
    /// <param name="country">The country.</param>
    /// <param name="validator">The validator, when this method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when a validator is registered for the country.</returns>
    public bool TryGetValidator(CountryCode country, [NotNullWhen(true)] out INationalIdValidator? validator) =>
        _validators.TryGetValue(country.Value, out validator);
}
