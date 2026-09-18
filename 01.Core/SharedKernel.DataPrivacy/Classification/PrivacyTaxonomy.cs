using Microsoft.Extensions.Compliance.Classification;

namespace SharedKernel.DataPrivacy.Classification;

/// <summary>
/// The platform's personal-data taxonomy: one <see cref="DataClassification"/> per kind of
/// personal data named by GDPR (Articles 4, 9 and 10) and KVKK (Articles 3 and 6).
/// </summary>
/// <remarks>
/// <para>
/// Each classification has a matching attribute (<see cref="EmailAddressDataAttribute"/>,
/// <see cref="HealthDataAttribute"/>, ...) and, through
/// <c>SetPrivacyRedactors</c> (<see cref="Redaction.PrivacyRedactionBuilderExtensions"/>), a redactor, so a
/// <c>[LoggerMessage]</c> parameter or a <c>[LogProperties]</c> member marked with the attribute is
/// masked when it is logged.
/// </para>
/// <para>
/// A service adds its own kinds by creating another <see cref="DataClassification"/> under
/// <see cref="TaxonomyName"/> (or its own taxonomy) and deriving an attribute from
/// <see cref="DataClassificationAttribute"/>; see the package README.
/// </para>
/// </remarks>
public static class PrivacyTaxonomy
{
    /// <summary>The taxonomy name every classification in this class carries.</summary>
    public const string TaxonomyName = "SharedKernel.Privacy";

    /// <summary>A person's name, surname or initials.</summary>
    public static DataClassification PersonName { get; } = new(TaxonomyName, nameof(PersonName));

    /// <summary>An email address.</summary>
    public static DataClassification EmailAddress { get; } = new(TaxonomyName, nameof(EmailAddress));

    /// <summary>A phone number.</summary>
    public static DataClassification PhoneNumber { get; } = new(TaxonomyName, nameof(PhoneNumber));

    /// <summary>A postal or home address.</summary>
    public static DataClassification PostalAddress { get; } = new(TaxonomyName, nameof(PostalAddress));

    /// <summary>A date of birth.</summary>
    public static DataClassification DateOfBirth { get; } = new(TaxonomyName, nameof(DateOfBirth));

    /// <summary>A government-issued identity number: national ID (TCKN), passport, tax or social security number.</summary>
    public static DataClassification NationalId { get; } = new(TaxonomyName, nameof(NationalId));

    /// <summary>
    /// An identifier that singles out a person online: user id, customer number, device id,
    /// cookie or advertising id. Pseudonymized, not erased, so log lines stay correlatable.
    /// </summary>
    public static DataClassification OnlineIdentifier { get; } = new(TaxonomyName, nameof(OnlineIdentifier));

    /// <summary>An IP address.</summary>
    public static DataClassification IpAddress { get; } = new(TaxonomyName, nameof(IpAddress));

    /// <summary>A precise location: coordinates, geohash, cell or Wi-Fi location.</summary>
    public static DataClassification Location { get; } = new(TaxonomyName, nameof(Location));

    /// <summary>A bank account number or IBAN.</summary>
    public static DataClassification BankAccount { get; } = new(TaxonomyName, nameof(BankAccount));

    /// <summary>A payment card number (PAN), expiry or cardholder name. The CVV must never be stored or logged at all.</summary>
    public static DataClassification PaymentCard { get; } = new(TaxonomyName, nameof(PaymentCard));

    /// <summary>Other financial data about a person: income, balance, credit score, transaction history.</summary>
    public static DataClassification Financial { get; } = new(TaxonomyName, nameof(Financial));

    /// <summary>A password, API key, token, recovery code or security answer.</summary>
    public static DataClassification Credential { get; } = new(TaxonomyName, nameof(Credential));

    /// <summary>Special category: data about physical or mental health, including disability and medical records.</summary>
    public static DataClassification Health { get; } = new(TaxonomyName, nameof(Health));

    /// <summary>Special category: genetic data.</summary>
    public static DataClassification Genetic { get; } = new(TaxonomyName, nameof(Genetic));

    /// <summary>Special category: biometric data used to identify a person (fingerprint, face or voice template).</summary>
    public static DataClassification Biometric { get; } = new(TaxonomyName, nameof(Biometric));

    /// <summary>Special category: racial or ethnic origin.</summary>
    public static DataClassification EthnicOrigin { get; } = new(TaxonomyName, nameof(EthnicOrigin));

    /// <summary>Special category: political opinions.</summary>
    public static DataClassification PoliticalOpinion { get; } = new(TaxonomyName, nameof(PoliticalOpinion));

    /// <summary>Special category: religious, philosophical or other beliefs, including sect.</summary>
    public static DataClassification Belief { get; } = new(TaxonomyName, nameof(Belief));

    /// <summary>Special category: trade union membership (GDPR), or membership of an association, foundation or trade union (KVKK).</summary>
    public static DataClassification Membership { get; } = new(TaxonomyName, nameof(Membership));

    /// <summary>Special category: sex life or sexual orientation.</summary>
    public static DataClassification SexLife { get; } = new(TaxonomyName, nameof(SexLife));

    /// <summary>Special category: criminal convictions, offences and security measures (GDPR Article 10, KVKK Article 6).</summary>
    public static DataClassification CriminalRecord { get; } = new(TaxonomyName, nameof(CriminalRecord));

    /// <summary>Special category under KVKK only: appearance and dress (for example, headscarf in a photo record).</summary>
    public static DataClassification Appearance { get; } = new(TaxonomyName, nameof(Appearance));

    /// <summary>
    /// The special categories of GDPR Articles 9 and 10 and KVKK Article 6, which need an explicit
    /// legal basis to process and are always erased, never partly shown, when logged.
    /// </summary>
    public static DataClassificationSet SpecialCategories { get; } = new(
        Health, Genetic, Biometric, EthnicOrigin, PoliticalOpinion, Belief, Membership, SexLife, CriminalRecord, Appearance);

    /// <summary>Every classification in this taxonomy.</summary>
    public static IReadOnlyList<DataClassification> All { get; } =
    [
        PersonName, EmailAddress, PhoneNumber, PostalAddress, DateOfBirth, NationalId, OnlineIdentifier, IpAddress,
        Location, BankAccount, PaymentCard, Financial, Credential,
        Health, Genetic, Biometric, EthnicOrigin, PoliticalOpinion, Belief, Membership, SexLife, CriminalRecord, Appearance,
    ];

    /// <summary>Returns whether <paramref name="classification"/> is one of the <see cref="SpecialCategories"/>.</summary>
    /// <param name="classification">The classification to test.</param>
    /// <returns><see langword="true"/> for a special category; otherwise <see langword="false"/>.</returns>
    public static bool IsSpecialCategory(DataClassification classification) =>
        classification.TaxonomyName == TaxonomyName && SpecialCategoryValues.Contains(classification.Value);

    private static readonly HashSet<string> SpecialCategoryValues = new(StringComparer.Ordinal)
    {
        nameof(Health), nameof(Genetic), nameof(Biometric), nameof(EthnicOrigin), nameof(PoliticalOpinion),
        nameof(Belief), nameof(Membership), nameof(SexLife), nameof(CriminalRecord), nameof(Appearance),
    };
}
