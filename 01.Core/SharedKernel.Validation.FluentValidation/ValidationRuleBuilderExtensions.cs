using FluentValidation;
using FluentValidation.Results;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Validation.FluentValidation;

/// <summary>
/// FluentValidation rules for the <c>SharedKernel.Validation</c> identifier types:
/// <c>RuleFor(x =&gt; x.Iban).MustBeValidIban()</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>A null value passes</b>, as with FluentValidation's own format rules. Add <c>NotEmpty()</c>
/// when the field is required. An empty or whitespace-only string fails with
/// <c>validation.required</c>.
/// </para>
/// <para>
/// <b>Each failure carries the identifier's own error:</b> its specific code (for example
/// <c>validation.iban.invalid_length</c>, not one code per rule), its message, and its values as
/// message placeholders (<c>{country}</c>, <c>{expected}</c>, <c>{actual}</c>) next to
/// FluentValidation's <c>{PropertyName}</c> and <c>{PropertyPath}</c>. <c>SharedKernel.Application</c>'s
/// validation behavior passes all of them on, so the HTTP response can translate the message and key
/// it by field. The failure's <c>CustomState</c> is the <see cref="Error"/> itself.
/// </para>
/// <para>
/// <b>The rejected value is never attached</b> to the failure (<c>AttemptedValue</c> is null and
/// there is no <c>{PropertyValue}</c> placeholder), so a card or national ID number cannot reach a
/// log through a validation result.
/// </para>
/// <para>
/// Because each failure has its own code, the rules are built with FluentValidation's <c>Custom</c>,
/// which is the only way to set the code per failure. As a consequence <c>WithMessage</c>,
/// <c>WithErrorCode</c>, <c>WithName</c> and <c>WithSeverity</c> are not available after these rules;
/// <c>When</c> and <c>Unless</c> are. Reword a message by translating its code, not per rule; the
/// field name in <c>{PropertyName}</c> comes from the property, or from <c>WithName</c> placed on an
/// earlier rule of the same property.
/// </para>
/// </remarks>
public static class ValidationRuleBuilderExtensions
{
#nullable disable annotations // Like FluentValidation's own rules: fits string and string? properties without warnings.

    /// <summary>The value must be a valid <see cref="Iban"/>.</summary>
    /// <typeparam name="T">The validated type.</typeparam>
    /// <param name="ruleBuilder">The rule builder.</param>
    /// <param name="allowUnregisteredCountry">See <see cref="Iban.Create(string?, bool)"/>.</param>
    /// <returns>The rule builder, for <c>When</c> and <c>Unless</c>.</returns>
    public static IRuleBuilderOptionsConditions<T, string> MustBeValidIban<T>(
        this IRuleBuilder<T, string> ruleBuilder,
        bool allowUnregisteredCountry = false) =>
        Attach(ruleBuilder, (_, value) => ToResult(Iban.Create(value, allowUnregisteredCountry)));

    /// <summary>The value must be a valid <see cref="Bic"/>.</summary>
    /// <typeparam name="T">The validated type.</typeparam>
    /// <param name="ruleBuilder">The rule builder.</param>
    /// <returns>The rule builder, for <c>When</c> and <c>Unless</c>.</returns>
    public static IRuleBuilderOptionsConditions<T, string> MustBeValidBic<T>(this IRuleBuilder<T, string> ruleBuilder) =>
        MustBeValid<T, Bic>(ruleBuilder);

    /// <summary>The value must be a valid <see cref="CardNumber"/>.</summary>
    /// <typeparam name="T">The validated type.</typeparam>
    /// <param name="ruleBuilder">The rule builder.</param>
    /// <param name="requireKnownNetwork">See <see cref="CardNumber.Create(string?, bool)"/>.</param>
    /// <returns>The rule builder, for <c>When</c> and <c>Unless</c>.</returns>
    public static IRuleBuilderOptionsConditions<T, string> MustBeValidCardNumber<T>(
        this IRuleBuilder<T, string> ruleBuilder,
        bool requireKnownNetwork = false) =>
        Attach(ruleBuilder, (_, value) => ToResult(CardNumber.Create(value, requireKnownNetwork)));

    /// <summary>The value must be a valid <see cref="CountryCode"/>.</summary>
    /// <typeparam name="T">The validated type.</typeparam>
    /// <param name="ruleBuilder">The rule builder.</param>
    /// <returns>The rule builder, for <c>When</c> and <c>Unless</c>.</returns>
    public static IRuleBuilderOptionsConditions<T, string> MustBeValidCountryCode<T>(this IRuleBuilder<T, string> ruleBuilder) =>
        MustBeValid<T, CountryCode>(ruleBuilder);

    /// <summary>The value must be a valid <see cref="CurrencyCode"/>.</summary>
    /// <typeparam name="T">The validated type.</typeparam>
    /// <param name="ruleBuilder">The rule builder.</param>
    /// <returns>The rule builder, for <c>When</c> and <c>Unless</c>.</returns>
    public static IRuleBuilderOptionsConditions<T, string> MustBeValidCurrencyCode<T>(this IRuleBuilder<T, string> ruleBuilder) =>
        MustBeValid<T, CurrencyCode>(ruleBuilder);

    /// <summary>The value must be a valid <see cref="PhoneNumber"/> in E.164 format.</summary>
    /// <typeparam name="T">The validated type.</typeparam>
    /// <param name="ruleBuilder">The rule builder.</param>
    /// <returns>The rule builder, for <c>When</c> and <c>Unless</c>.</returns>
    public static IRuleBuilderOptionsConditions<T, string> MustBeValidPhoneNumber<T>(this IRuleBuilder<T, string> ruleBuilder) =>
        MustBeValid<T, PhoneNumber>(ruleBuilder);

    /// <summary>The value must be a valid <see cref="Lei"/>.</summary>
    /// <typeparam name="T">The validated type.</typeparam>
    /// <param name="ruleBuilder">The rule builder.</param>
    /// <returns>The rule builder, for <c>When</c> and <c>Unless</c>.</returns>
    public static IRuleBuilderOptionsConditions<T, string> MustBeValidLei<T>(this IRuleBuilder<T, string> ruleBuilder) =>
        MustBeValid<T, Lei>(ruleBuilder);

    /// <summary>The value must be a valid <see cref="AbaRoutingNumber"/>.</summary>
    /// <typeparam name="T">The validated type.</typeparam>
    /// <param name="ruleBuilder">The rule builder.</param>
    /// <returns>The rule builder, for <c>When</c> and <c>Unless</c>.</returns>
    public static IRuleBuilderOptionsConditions<T, string> MustBeValidAbaRoutingNumber<T>(this IRuleBuilder<T, string> ruleBuilder) =>
        MustBeValid<T, AbaRoutingNumber>(ruleBuilder);

    /// <summary>The value must be a valid <see cref="SepaCreditorId"/>.</summary>
    /// <typeparam name="T">The validated type.</typeparam>
    /// <param name="ruleBuilder">The rule builder.</param>
    /// <returns>The rule builder, for <c>When</c> and <c>Unless</c>.</returns>
    public static IRuleBuilderOptionsConditions<T, string> MustBeValidSepaCreditorId<T>(this IRuleBuilder<T, string> ruleBuilder) =>
        MustBeValid<T, SepaCreditorId>(ruleBuilder);

    /// <summary>The value must be a valid <see cref="VatNumber"/> that starts with its country prefix.</summary>
    /// <typeparam name="T">The validated type.</typeparam>
    /// <param name="ruleBuilder">The rule builder.</param>
    /// <returns>The rule builder, for <c>When</c> and <c>Unless</c>.</returns>
    public static IRuleBuilderOptionsConditions<T, string> MustBeValidVatNumber<T>(this IRuleBuilder<T, string> ruleBuilder) =>
        MustBeValid<T, VatNumber>(ruleBuilder);

    /// <summary>
    /// The value must be a valid VAT number of the country another property holds, with or without
    /// the prefix: a Turkish form's VKN field next to its country field.
    /// </summary>
    /// <typeparam name="T">The validated type.</typeparam>
    /// <param name="ruleBuilder">The rule builder.</param>
    /// <param name="countrySelector">Reads the ISO 3166-1 country code from the validated object.</param>
    /// <returns>The rule builder, for <c>When</c> and <c>Unless</c>.</returns>
    /// <remarks>When the country is missing or not a valid code the rule is skipped; validate the country field with its own rule.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="countrySelector"/> is null.</exception>
    public static IRuleBuilderOptionsConditions<T, string> MustBeValidVatNumber<T>(
        this IRuleBuilder<T, string> ruleBuilder,
        Func<T, string> countrySelector)
    {
        ArgumentNullException.ThrowIfNull(countrySelector);

        return Attach(ruleBuilder, (instance, value) =>
            CountryCode.Create(countrySelector(instance)) is { IsSuccess: true } country
                ? ToResult(VatNumber.Create(country.Value, value))
                : Result.Success());
    }

    /// <summary>The value must be a valid national ID of the country another property holds.</summary>
    /// <typeparam name="T">The validated type.</typeparam>
    /// <param name="ruleBuilder">The rule builder.</param>
    /// <param name="countrySelector">Reads the ISO 3166-1 country code from the validated object.</param>
    /// <param name="registry">The validators to use; <see cref="NationalIdValidatorRegistry.Default"/> when omitted.</param>
    /// <returns>The rule builder, for <c>When</c> and <c>Unless</c>.</returns>
    /// <remarks>
    /// When the country is missing or not a valid code the rule is skipped; validate the country
    /// field with its own rule. A valid country without a registered validator fails with
    /// <c>validation.national_id.unsupported_country</c>.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="countrySelector"/> is null.</exception>
    public static IRuleBuilderOptionsConditions<T, string> MustBeValidNationalId<T>(
        this IRuleBuilder<T, string> ruleBuilder,
        Func<T, string> countrySelector,
        NationalIdValidatorRegistry registry = null)
    {
        ArgumentNullException.ThrowIfNull(countrySelector);

        return Attach(ruleBuilder, (instance, value) =>
            CountryCode.Create(countrySelector(instance)) is { IsSuccess: true } country
                ? ToResult(NationalId.Create(country.Value, value, registry))
                : Result.Success());
    }

    /// <summary>The value must be a valid <typeparamref name="TValue"/>, for any identifier type, including your own.</summary>
    /// <typeparam name="T">The validated type.</typeparam>
    /// <typeparam name="TValue">The identifier type.</typeparam>
    /// <param name="ruleBuilder">The rule builder.</param>
    /// <returns>The rule builder, for <c>When</c> and <c>Unless</c>.</returns>
    public static IRuleBuilderOptionsConditions<T, string> MustBeValid<T, TValue>(this IRuleBuilder<T, string> ruleBuilder)
        where TValue : struct, IValidatedValue<TValue> =>
        Attach(ruleBuilder, (_, value) => ToResult(TValue.Create(value)));

#nullable restore annotations

    private static Result ToResult<TValue>(Result<TValue> result) => result.IsSuccess ? Result.Success() : result.Error;

    private static IRuleBuilderOptionsConditions<T, string> Attach<T>(
        IRuleBuilder<T, string> ruleBuilder,
        Func<T, string, Result> validate)
    {
        ArgumentNullException.ThrowIfNull(ruleBuilder);

        return ruleBuilder.Custom((value, context) =>
        {
            if (value is null)
            {
                return;
            }

            Result result = validate(context.InstanceToValidate, value);
            if (result.IsSuccess)
            {
                return;
            }

            Error error = result.Error;
            var placeholders = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach ((string name, object? argument) in error.MessageArguments)
            {
                if (argument is not null)
                {
                    placeholders[name] = argument;
                }
            }

            placeholders[ErrorArgumentNames.PropertyName] = context.DisplayName;
            placeholders[ErrorArgumentNames.PropertyPath] = context.PropertyPath;

            context.AddFailure(new ValidationFailure(context.PropertyPath, error.Message, attemptedValue: null)
            {
                ErrorCode = error.Code,
                CustomState = error,
                FormattedMessagePlaceholderValues = placeholders,
            });
        });
    }
}
