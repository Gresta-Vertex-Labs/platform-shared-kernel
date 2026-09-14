using SharedKernel.Guards;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Validation.Errors;
using SharedKernel.Validation.NationalId;
using SharedKernel.Validation.Validators;

namespace SharedKernel.Validation.Guards;

/// <summary>
/// <see cref="Guard.Against"/> extension methods for the format validators in
/// <c>SharedKernel.Validation</c>. Functional path only (returns <see cref="Error"/>?, mirroring
/// every other guard in <c>SharedKernel.Guards</c>) — <c>Guard.Throw.*</c> parity is intentionally
/// out of scope. <c>SharedKernel.Guards</c>' <c>Guard.Throw</c> nested class is hand-enumerated
/// and hardcoded inside that package; extending it requires modifying <c>SharedKernel.Guards</c>
/// itself, which is out of this package's reach and was never requested by this phase's
/// acceptance criteria (the functional <c>Against.*</c> path only).
/// </summary>
public static class GuardValidationExtensions
{
    /// <summary>Returns a validation <see cref="Error"/> when <paramref name="value"/> is not a valid IBAN; otherwise <see langword="null"/>.</summary>
    public static Error? InvalidIban(this IGuardClause guard, string? value) =>
        ToGuardError(IbanValidator.Validate(value));

    /// <summary>Returns a validation <see cref="Error"/> when <paramref name="value"/> is not a valid BIC; otherwise <see langword="null"/>.</summary>
    public static Error? InvalidBic(this IGuardClause guard, string? value) =>
        ToGuardError(BicValidator.Validate(value));

    /// <summary>Returns a validation <see cref="Error"/> when <paramref name="value"/> fails PAN Luhn validation; otherwise <see langword="null"/>.</summary>
    public static Error? InvalidPan(this IGuardClause guard, string? value) =>
        ToGuardError(PanValidator.Validate(value));

    /// <summary>Returns a validation <see cref="Error"/> when <paramref name="value"/> is not a recognized ISO 4217 currency code; otherwise <see langword="null"/>.</summary>
    public static Error? InvalidCurrencyCode(this IGuardClause guard, string? value) =>
        ToGuardError(IsoCurrencyValidator.Validate(value));

    /// <summary>Returns a validation <see cref="Error"/> when <paramref name="value"/> is not a recognized ISO 3166-1 country code; otherwise <see langword="null"/>.</summary>
    public static Error? InvalidCountryCode(this IGuardClause guard, string? value) =>
        ToGuardError(IsoCountryValidator.Validate(value));

    /// <summary>Returns a validation <see cref="Error"/> when <paramref name="value"/> is not a well-formed E.164 phone number; otherwise <see langword="null"/>.</summary>
    public static Error? InvalidPhoneNumber(this IGuardClause guard, string? value) =>
        ToGuardError(E164PhoneValidator.Validate(value));

    /// <summary>Returns a validation <see cref="Error"/> when <paramref name="value"/> does not match the baseline VAT number format; otherwise <see langword="null"/>.</summary>
    public static Error? InvalidVatNumber(this IGuardClause guard, string? value) =>
        ToGuardError(VatValidator.Validate(value));

    /// <summary>Returns a validation <see cref="Error"/> when <paramref name="value"/> is not a valid LEI; otherwise <see langword="null"/>.</summary>
    public static Error? InvalidLei(this IGuardClause guard, string? value) =>
        ToGuardError(LeiValidator.Validate(value));

    /// <summary>Returns a validation <see cref="Error"/> when <paramref name="value"/> is not a valid ABA routing number; otherwise <see langword="null"/>.</summary>
    public static Error? InvalidAbaRoutingNumber(this IGuardClause guard, string? value) =>
        ToGuardError(AbaRoutingNumberValidator.Validate(value));

    /// <summary>Returns a validation <see cref="Error"/> when <paramref name="value"/> is not a valid SEPA Creditor Identifier; otherwise <see langword="null"/>.</summary>
    public static Error? InvalidSepaCreditorIdentifier(this IGuardClause guard, string? value) =>
        ToGuardError(SepaCreditorIdentifierValidator.Validate(value));

    /// <summary>
    /// Returns a validation <see cref="Error"/> when <paramref name="value"/> fails
    /// <paramref name="countryCode"/>'s registered national-identity-number checksum, or when no
    /// validator is registered for <paramref name="countryCode"/>; otherwise <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// The one guard requiring an explicit <paramref name="registry"/> instance parameter, since
    /// national-ID validation is registry-based rather than compile-time-generic like
    /// <c>Guard.Against.InvalidSmartEnum</c>.
    /// </remarks>
    public static Error? InvalidNationalId(
        this IGuardClause guard,
        string? value,
        string countryCode,
        INationalIdValidatorRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        if (string.IsNullOrWhiteSpace(value))
        {
            return Error.Validation(ValidationErrorCodes.NationalId.InvalidChecksum, "National ID must not be null or empty.");
        }

        if (!registry.TryGetValidator(countryCode, out INationalIdValidator? validator) || validator is null)
        {
            return Error.Validation(ValidationErrorCodes.NationalId.UnknownCountry,
                $"No national ID validator is registered for country code '{countryCode}'.");
        }

        return validator.IsValid(value)
            ? null
            : Error.Validation(ValidationErrorCodes.NationalId.InvalidChecksum,
                $"National ID failed the '{countryCode}' checksum validation.");
    }

    private static Error? ToGuardError(Result result) => result.IsFailure ? result.Error : null;
}
