using SharedKernel.Primitives.Results;

namespace SharedKernel.Validation;

/// <summary>
/// Checks national identity numbers for one country. Register implementations with
/// <see cref="ValidationServiceCollectionExtensions.AddNationalIdValidator{TValidator}"/> or pass
/// them to <see cref="NationalIdValidatorRegistry"/>.
/// </summary>
/// <remarks>
/// <see cref="TurkishNationalIdValidator"/> (TCKN) ships built in. Return
/// <see cref="ValidationMessages.NationalIdInvalidFormat"/> and
/// <see cref="ValidationMessages.NationalIdInvalidCheckDigit"/> errors so failures translate like the
/// built-in ones, and never put the number itself in an error message.
/// </remarks>
public interface INationalIdValidator
{
    /// <summary>Gets the country this validator checks.</summary>
    CountryCode Country { get; }

    /// <summary>Validates a number that has already been trimmed, has had spaces and hyphens removed, and is not empty.</summary>
    /// <param name="number">The number to check.</param>
    /// <returns>Success, or the reason the number is invalid.</returns>
    Result Validate(string number);
}
