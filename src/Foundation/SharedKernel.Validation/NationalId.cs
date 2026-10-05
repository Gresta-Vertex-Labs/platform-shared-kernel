using System.Diagnostics;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Validation.Internal;

namespace SharedKernel.Validation;

/// <summary>
/// A national identity number together with its country, checked by the country's
/// <see cref="INationalIdValidator"/>: <c>NationalId.Create(CountryCode.Parse("TR", null), "10000000146")</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b><see cref="ToString"/> is masked:</b> it shows only the last four characters, so a national
/// ID that reaches a log is not exposed. <see cref="Value"/> holds the full number; treat it as
/// personal data.
/// </para>
/// <para>
/// Unlike the other identifier types this one needs a country beside the value, so it does not
/// implement <see cref="IValidatedValue{TSelf}"/> and has no JSON converter. Store the country and
/// the number as two fields and call <see cref="Create"/> when reading them.
/// </para>
/// </remarks>
[DebuggerDisplay("{Country} {Masked,nq}")]
public readonly record struct NationalId
{
    private readonly string? _value;

    private NationalId(CountryCode country, string value)
    {
        Country = country;
        _value = value;
    }

    /// <summary>Gets the country that issued the number.</summary>
    public CountryCode Country { get; }

    /// <summary>Gets the full number, without spaces or hyphens. Handle it as personal data.</summary>
    public string Value => _value ?? string.Empty;

    /// <summary>Gets the number with everything but the last four characters replaced by <c>*</c>.</summary>
    public string Masked => Value.Length <= 4 ? new string('*', Value.Length) : new string('*', Value.Length - 4) + Value[^4..];

    /// <summary>Validates <paramref name="value"/> as a national ID of <paramref name="country"/>.</summary>
    /// <param name="country">The issuing country.</param>
    /// <param name="value">The number, with or without spaces and hyphens.</param>
    /// <param name="registry">The validators to use; <see cref="NationalIdValidatorRegistry.Default"/> when omitted.</param>
    /// <returns>The national ID, or the reason it is invalid.</returns>
    public static Result<NationalId> Create(CountryCode country, string? value, NationalIdValidatorRegistry? registry = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ValidationMessages.Required.ToError(ErrorType.Validation);
        }

        if (!(registry ?? NationalIdValidatorRegistry.Default).TryGetValidator(country, out INationalIdValidator? validator))
        {
            return ValidationMessages.NationalIdUnsupportedCountry.ToError(ErrorType.Validation, country.Value);
        }

        string number = Text.Compact(value, " -");
        Result result = validator.Validate(number);
        return result.IsSuccess ? new NationalId(country, number) : result.Error;
    }

    /// <summary>Returns <see cref="Masked"/>, never the full number.</summary>
    /// <returns>The masked number.</returns>
    public override string ToString() => Masked;
}
