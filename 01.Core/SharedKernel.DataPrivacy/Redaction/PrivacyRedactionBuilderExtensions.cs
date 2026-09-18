using Microsoft.Extensions.Compliance.Classification;
using Microsoft.Extensions.Compliance.Redaction;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharedKernel.DataPrivacy.Classification;
using SharedKernel.DataPrivacy.Masking;

namespace SharedKernel.DataPrivacy.Redaction;

/// <summary>Registers a redactor for every classification in <see cref="PrivacyTaxonomy"/>.</summary>
public static class PrivacyRedactionBuilderExtensions
{
    /// <summary>
    /// Maps every <see cref="PrivacyTaxonomy"/> classification to a redactor. Identifiers that can be
    /// partly shown are masked with the matching <see cref="PiiMasking"/> rule; everything else,
    /// including every special category and <see cref="PrivacyTaxonomy.OnlineIdentifier"/>, becomes
    /// <see cref="PiiMasking.RedactedSentinel"/>.
    /// </summary>
    /// <param name="builder">The builder passed to <c>services.AddRedaction(...)</c>.</param>
    /// <returns><paramref name="builder"/>, for chaining.</returns>
    /// <remarks>
    /// Classifications of other taxonomies are left to the builder's fallback redactor. Call
    /// <c>SetRedactor</c> after this method to change the redactor of one classification.
    /// </remarks>
    public static IRedactionBuilder SetPrivacyRedactors(this IRedactionBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.SetMaskingRedactors().SetRedactor<SuppressingRedactor>(Suppressed(includeOnlineIdentifier: true));
    }

    /// <summary>
    /// Maps every <see cref="PrivacyTaxonomy"/> classification to a redactor, like
    /// <see cref="SetPrivacyRedactors(IRedactionBuilder)"/>, but replaces
    /// <see cref="PrivacyTaxonomy.OnlineIdentifier"/> values with a <paramref name="pseudonymizer"/>
    /// token so log lines about one user can still be correlated.
    /// </summary>
    /// <param name="builder">The builder passed to <c>services.AddRedaction(...)</c>.</param>
    /// <param name="pseudonymizer">The pseudonymizer, holding a key kept in a secret store.</param>
    /// <returns><paramref name="builder"/>, for chaining.</returns>
    public static IRedactionBuilder SetPrivacyRedactors(this IRedactionBuilder builder, Pseudonymizer pseudonymizer)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(pseudonymizer);

        builder.Services.TryAddSingleton(pseudonymizer);
        return builder
            .SetMaskingRedactors()
            .SetRedactor<PseudonymizingRedactor>(PrivacyTaxonomy.OnlineIdentifier)
            .SetRedactor<SuppressingRedactor>(Suppressed(includeOnlineIdentifier: false));
    }

    private static IRedactionBuilder SetMaskingRedactors(this IRedactionBuilder builder) => builder
        .SetRedactor<PersonNameRedactor>(PrivacyTaxonomy.PersonName)
        .SetRedactor<EmailRedactor>(PrivacyTaxonomy.EmailAddress)
        .SetRedactor<PhoneNumberRedactor>(PrivacyTaxonomy.PhoneNumber)
        .SetRedactor<NationalIdRedactor>(PrivacyTaxonomy.NationalId)
        .SetRedactor<IpAddressRedactor>(PrivacyTaxonomy.IpAddress)
        .SetRedactor<BankAccountRedactor>(PrivacyTaxonomy.BankAccount)
        .SetRedactor<CardNumberRedactor>(PrivacyTaxonomy.PaymentCard);

    private static DataClassificationSet[] Suppressed(bool includeOnlineIdentifier)
    {
        DataClassification[] masked =
        [
            PrivacyTaxonomy.PersonName, PrivacyTaxonomy.EmailAddress, PrivacyTaxonomy.PhoneNumber, PrivacyTaxonomy.NationalId,
            PrivacyTaxonomy.IpAddress, PrivacyTaxonomy.BankAccount, PrivacyTaxonomy.PaymentCard,
        ];

        return PrivacyTaxonomy.All
            .Where(c => !masked.Contains(c) && (includeOnlineIdentifier || c != PrivacyTaxonomy.OnlineIdentifier))
            .Select(c => new DataClassificationSet(c))
            .ToArray();
    }
}
