using FluentValidation;
using FluentValidation.Results;
using SharedKernel.Primitives.Results;
using SharedKernel.Validation.Errors;
using SharedKernel.Validation.NationalId;
using SharedKernel.Validation.Validators;

namespace SharedKernel.Validation.FluentValidation;

/// <summary>
/// <see cref="IRuleBuilder{T,TProperty}"/> extension methods adapting every
/// <c>SharedKernel.Validation</c> static format validator into a FluentValidation rule.
/// </summary>
/// <remarks>
/// <para>
/// Each <c>.MustBeValid*()</c> rule delegates to the matching <c>SharedKernel.Validation</c>
/// static validator's <c>Validate(string?)</c> method and, on failure, adds a single
/// <see cref="ValidationFailure"/> whose <see cref="ValidationFailure.ErrorCode"/> is set to the
/// <em>exact</em> <see cref="ValidationErrorCodes"/> constant the underlying validator itself
/// returned (never a single hardcoded code per rule) — a failure therefore surfaces the identical
/// error code whether reached via the standalone <c>SharedKernel.Validation</c> call or through
/// this adapter. This matters for validators such as <see cref="IbanValidator"/>, whose
/// <c>Validate</c> can fail with three distinct codes (<see cref="ValidationErrorCodes.Iban.InvalidFormat"/>,
/// <see cref="ValidationErrorCodes.Iban.InvalidCheckDigit"/>, <see cref="ValidationErrorCodes.Iban.InvalidLength"/>) —
/// a rule that always attached the same fixed error code would silently break that parity.
/// </para>
/// <para>
/// Every rule is built on FluentValidation's <c>Custom(...)</c> rule-builder extension, which adds
/// its own <see cref="ValidationFailure"/> directly rather than evaluating a boolean predicate
/// through the normal message/error-code pipeline. Consequently chaining <c>.WithMessage(...)</c>
/// or <c>.WithErrorCode(...)</c> after any of these rules has NO EFFECT — the message and error
/// code are always the ones the underlying <c>SharedKernel.Validation</c> validator produced.
/// <c>.When(...)</c>/<c>.Unless(...)</c> and other rule-level conditions still apply normally.
/// </para>
/// <para>
/// <b>Composition with <c>05.Application.Behaviors</c>'s <c>ValidationBehavior</c>:</b> a service's
/// <c>AbstractValidator&lt;TCommand&gt;</c> calls these rules exactly like any other FluentValidation
/// rule — no extra plumbing is required for them to participate in a MediatR pipeline already
/// wired with <c>ValidationBehavior</c>:
/// <code>
/// public sealed class CreatePaymentCommandValidator : AbstractValidator&lt;CreatePaymentCommand&gt;
/// {
///     public CreatePaymentCommandValidator(INationalIdValidatorRegistry nationalIdRegistry)
///     {
///         RuleFor(x => x.Iban).MustBeValidIban();
///         RuleFor(x => x.Bic).MustBeValidBic();
///         RuleFor(x => x.CurrencyCode).MustBeValidCurrencyCode();
///         RuleFor(x => x.PayerNationalId)
///             .MustBeValidNationalId(x => x.PayerCountryCode, nationalIdRegistry);
///     }
/// }
/// </code>
/// A failing rule surfaces as a <see cref="ValidationFailure"/> whose <see cref="ValidationFailure.ErrorCode"/>
/// carries the matching <see cref="ValidationErrorCodes"/> constant — <c>ValidationBehavior</c>
/// enumerates that same <c>IValidator&lt;TRequest&gt;.ValidateAsync(...)</c> result for every rule
/// regardless of how it was built, so these rules participate with zero extra plumbing. As of this
/// writing, <c>ValidationBehavior</c> itself projects each failure via
/// <c>Error.Validation(failure.PropertyName, failure.ErrorMessage)</c> — the property name becomes
/// the downstream <c>Error.Code</c>, not <see cref="ValidationFailure.ErrorCode"/> — so a consumer
/// that wants the finer-grained <see cref="ValidationErrorCodes"/> constant on the outward-facing
/// <c>Error</c> reads <see cref="ValidationFailure.ErrorCode"/> directly (e.g. from a custom
/// exception handler, or a service-local pipeline behavior variant); that mapping decision belongs
/// to the consuming service or to a future <c>05.Application.Behaviors</c> enhancement, not to this
/// package.
/// </para>
/// </remarks>
public static class ValidationRuleBuilderExtensions
{
    /// <summary>Adds a rule requiring the property to be a well-formed, checksum-valid IBAN (see <see cref="IbanValidator"/>).</summary>
    /// <typeparam name="T">The type under validation.</typeparam>
    /// <param name="ruleBuilder">The rule builder to extend.</param>
    /// <param name="allowFallbackForUnknownCountry">
    /// Forwarded verbatim to <see cref="IbanValidator.Validate(string?, bool)"/>. Defaults to
    /// <see langword="false"/>, preserving this rule's original behavior exactly: an unrecognized
    /// country prefix hard-rejects. When explicitly <see langword="true"/>, an unrecognized country
    /// prefix skips only the country-specific exact-length check and instead falls back to ISO
    /// 13616's general shape/length bound plus the mod-97 check-digit algorithm alone — see
    /// <see cref="IbanValidator"/>'s own XML docs for exactly what this trades away.
    /// </param>
    /// <returns>The same rule builder, for chaining rule-level conditions such as <c>.When(...)</c>.</returns>
    public static IRuleBuilderOptionsConditions<T, string> MustBeValidIban<T>(
        this IRuleBuilder<T, string> ruleBuilder,
        bool allowFallbackForUnknownCountry = false) =>
        Attach(ruleBuilder, value => IbanValidator.Validate(value, allowFallbackForUnknownCountry));

    /// <summary>Adds a rule requiring the property to be a well-formed BIC/SWIFT code (see <see cref="BicValidator"/>).</summary>
    /// <typeparam name="T">The type under validation.</typeparam>
    /// <param name="ruleBuilder">The rule builder to extend.</param>
    /// <returns>The same rule builder, for chaining rule-level conditions such as <c>.When(...)</c>.</returns>
    public static IRuleBuilderOptionsConditions<T, string> MustBeValidBic<T>(this IRuleBuilder<T, string> ruleBuilder) =>
        Attach(ruleBuilder, BicValidator.Validate);

    /// <summary>Adds a rule requiring the property to be a Luhn-valid payment-card PAN (see <see cref="PanValidator"/>).</summary>
    /// <typeparam name="T">The type under validation.</typeparam>
    /// <param name="ruleBuilder">The rule builder to extend.</param>
    /// <returns>The same rule builder, for chaining rule-level conditions such as <c>.When(...)</c>.</returns>
    public static IRuleBuilderOptionsConditions<T, string> MustBeValidPan<T>(this IRuleBuilder<T, string> ruleBuilder) =>
        Attach(ruleBuilder, PanValidator.Validate);

    /// <summary>Adds a rule requiring the property to be a recognized ISO 4217 currency code (see <see cref="IsoCurrencyValidator"/>).</summary>
    /// <typeparam name="T">The type under validation.</typeparam>
    /// <param name="ruleBuilder">The rule builder to extend.</param>
    /// <returns>The same rule builder, for chaining rule-level conditions such as <c>.When(...)</c>.</returns>
    public static IRuleBuilderOptionsConditions<T, string> MustBeValidCurrencyCode<T>(this IRuleBuilder<T, string> ruleBuilder) =>
        Attach(ruleBuilder, IsoCurrencyValidator.Validate);

    /// <summary>Adds a rule requiring the property to be a recognized ISO 3166-1 alpha-2 country code (see <see cref="IsoCountryValidator"/>).</summary>
    /// <typeparam name="T">The type under validation.</typeparam>
    /// <param name="ruleBuilder">The rule builder to extend.</param>
    /// <returns>The same rule builder, for chaining rule-level conditions such as <c>.When(...)</c>.</returns>
    public static IRuleBuilderOptionsConditions<T, string> MustBeValidCountryCode<T>(this IRuleBuilder<T, string> ruleBuilder) =>
        Attach(ruleBuilder, IsoCountryValidator.Validate);

    /// <summary>Adds a rule requiring the property to be a well-formed E.164 phone number (see <see cref="E164PhoneValidator"/>).</summary>
    /// <typeparam name="T">The type under validation.</typeparam>
    /// <param name="ruleBuilder">The rule builder to extend.</param>
    /// <returns>The same rule builder, for chaining rule-level conditions such as <c>.When(...)</c>.</returns>
    public static IRuleBuilderOptionsConditions<T, string> MustBeValidPhoneNumber<T>(this IRuleBuilder<T, string> ruleBuilder) =>
        Attach(ruleBuilder, E164PhoneValidator.Validate);

    /// <summary>Adds a rule requiring the property to match the baseline cross-jurisdiction VAT number format (see <see cref="VatValidator"/>).</summary>
    /// <typeparam name="T">The type under validation.</typeparam>
    /// <param name="ruleBuilder">The rule builder to extend.</param>
    /// <returns>The same rule builder, for chaining rule-level conditions such as <c>.When(...)</c>.</returns>
    public static IRuleBuilderOptionsConditions<T, string> MustBeValidVatNumber<T>(this IRuleBuilder<T, string> ruleBuilder) =>
        Attach(ruleBuilder, VatValidator.Validate);

    /// <summary>Adds a rule requiring the property to be a well-formed, checksum-valid LEI (see <see cref="LeiValidator"/>).</summary>
    /// <typeparam name="T">The type under validation.</typeparam>
    /// <param name="ruleBuilder">The rule builder to extend.</param>
    /// <returns>The same rule builder, for chaining rule-level conditions such as <c>.When(...)</c>.</returns>
    public static IRuleBuilderOptionsConditions<T, string> MustBeValidLei<T>(this IRuleBuilder<T, string> ruleBuilder) =>
        Attach(ruleBuilder, LeiValidator.Validate);

    /// <summary>Adds a rule requiring the property to be a well-formed, checksum-valid US ABA routing number (see <see cref="AbaRoutingNumberValidator"/>).</summary>
    /// <typeparam name="T">The type under validation.</typeparam>
    /// <param name="ruleBuilder">The rule builder to extend.</param>
    /// <returns>The same rule builder, for chaining rule-level conditions such as <c>.When(...)</c>.</returns>
    public static IRuleBuilderOptionsConditions<T, string> MustBeValidAbaRoutingNumber<T>(this IRuleBuilder<T, string> ruleBuilder) =>
        Attach(ruleBuilder, AbaRoutingNumberValidator.Validate);

    /// <summary>Adds a rule requiring the property to be a well-formed, checksum-valid SEPA Creditor Identifier (see <see cref="SepaCreditorIdentifierValidator"/>).</summary>
    /// <typeparam name="T">The type under validation.</typeparam>
    /// <param name="ruleBuilder">The rule builder to extend.</param>
    /// <returns>The same rule builder, for chaining rule-level conditions such as <c>.When(...)</c>.</returns>
    public static IRuleBuilderOptionsConditions<T, string> MustBeValidSepaCreditorIdentifier<T>(this IRuleBuilder<T, string> ruleBuilder) =>
        Attach(ruleBuilder, SepaCreditorIdentifierValidator.Validate);

    /// <summary>
    /// Adds a rule requiring the property to pass the national-identity-number checksum registered
    /// for the country resolved by <paramref name="countryCodeSelector"/> (see
    /// <see cref="INationalIdValidatorRegistry"/>).
    /// </summary>
    /// <remarks>
    /// The one rule requiring extra parameters, since national-ID validation is registry-based
    /// rather than a single fixed algorithm — mirroring
    /// <c>SharedKernel.Validation.Guards.GuardValidationExtensions.InvalidNationalId</c>'s exact
    /// same shape and failure-code behavior: an unregistered country code fails with
    /// <see cref="ValidationErrorCodes.NationalId.UnknownCountry"/>, a registered country whose
    /// validator rejects the value fails with <see cref="ValidationErrorCodes.NationalId.InvalidChecksum"/>.
    /// </remarks>
    /// <typeparam name="T">The type under validation.</typeparam>
    /// <param name="ruleBuilder">The rule builder to extend.</param>
    /// <param name="countryCodeSelector">
    /// Resolves the ISO 3166-1 alpha-2 country code (from the instance under validation) whose
    /// registered <see cref="INationalIdValidator"/> should check the property's value.
    /// </param>
    /// <param name="registry">The registry to resolve the country's <see cref="INationalIdValidator"/> from.</param>
    /// <returns>The same rule builder, for chaining rule-level conditions such as <c>.When(...)</c>.</returns>
    public static IRuleBuilderOptionsConditions<T, string> MustBeValidNationalId<T>(
        this IRuleBuilder<T, string> ruleBuilder,
        Func<T, string> countryCodeSelector,
        INationalIdValidatorRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(ruleBuilder);
        ArgumentNullException.ThrowIfNull(countryCodeSelector);
        ArgumentNullException.ThrowIfNull(registry);

        return ruleBuilder.Custom((value, context) =>
        {
            string countryCode = countryCodeSelector(context.InstanceToValidate);

            if (!registry.TryGetValidator(countryCode, out INationalIdValidator? validator) || validator is null)
            {
                context.AddFailure(new ValidationFailure(
                    context.PropertyPath,
                    $"No national ID validator is registered for country code '{countryCode}'.")
                {
                    ErrorCode = ValidationErrorCodes.NationalId.UnknownCountry,
                });

                return;
            }

            if (string.IsNullOrWhiteSpace(value) || !validator.IsValid(value))
            {
                context.AddFailure(new ValidationFailure(
                    context.PropertyPath,
                    $"National ID failed the '{countryCode}' checksum validation.")
                {
                    ErrorCode = ValidationErrorCodes.NationalId.InvalidChecksum,
                });
            }
        });
    }

    // Shared plumbing for every single-parameter rule: run the SharedKernel.Validation static
    // validator's Validate(string?), and on failure attach a ValidationFailure carrying the exact
    // Error.Code/Error.Message the validator produced — never a rule-fixed error code, since several
    // validators (IbanValidator most notably) can fail with more than one distinct code.
    private static IRuleBuilderOptionsConditions<T, string> Attach<T>(
        IRuleBuilder<T, string> ruleBuilder,
        Func<string?, Result> validate)
    {
        ArgumentNullException.ThrowIfNull(ruleBuilder);
        ArgumentNullException.ThrowIfNull(validate);

        return ruleBuilder.Custom((value, context) =>
        {
            Result result = validate(value);

            if (result.IsFailure)
            {
                context.AddFailure(new ValidationFailure(context.PropertyPath, result.Error.Message)
                {
                    ErrorCode = result.Error.Code,
                });
            }
        });
    }
}
