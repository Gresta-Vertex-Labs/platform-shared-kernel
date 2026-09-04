using System.Collections.Concurrent;

namespace SharedKernel.Validation.NationalId;

/// <summary>
/// The default <see cref="INationalIdValidatorRegistry"/> implementation — a thread-safe,
/// <see cref="ConcurrentDictionary{TKey,TValue}"/>-backed registry pre-seeded with
/// <see cref="TckNationalIdValidator"/> at country code <c>"TR"</c>.
/// </summary>
public sealed class NationalIdValidatorRegistry : INationalIdValidatorRegistry
{
    private readonly ConcurrentDictionary<string, INationalIdValidator> _validators = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates a new registry pre-seeded with the built-in <see cref="TckNationalIdValidator"/> ("TR").</summary>
    public NationalIdValidatorRegistry()
    {
        Register(new TckNationalIdValidator());
    }

    /// <inheritdoc />
    public bool TryGetValidator(string countryCode, out INationalIdValidator? validator)
    {
        ArgumentNullException.ThrowIfNull(countryCode);

        return _validators.TryGetValue(countryCode, out validator);
    }

    /// <summary>
    /// Registers or replaces the validator for <paramref name="validator"/>'s
    /// <see cref="INationalIdValidator.CountryCode"/>.
    /// </summary>
    /// <remarks>
    /// Internal — the sanctioned way for a consuming service to add a country is
    /// <c>services.AddSharedKernelValidation().AddNationalIdValidator&lt;TValidator&gt;()</c>,
    /// which resolves every DI-registered <see cref="INationalIdValidator"/> and calls this
    /// method on the consuming service's behalf when the registry singleton is constructed.
    /// </remarks>
    internal void Register(INationalIdValidator validator)
    {
        ArgumentNullException.ThrowIfNull(validator);

        _validators[validator.CountryCode] = validator;
    }
}
