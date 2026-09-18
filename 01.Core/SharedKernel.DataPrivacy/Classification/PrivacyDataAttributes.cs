using Microsoft.Extensions.Compliance.Classification;

namespace SharedKernel.DataPrivacy.Classification;

/// <summary>Marks a person's name: <see cref="PrivacyTaxonomy.PersonName"/>.</summary>
/// <remarks>On a positional record parameter, write <c>[property: PersonNameData]</c> so the generated property carries it.</remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.ReturnValue, AllowMultiple = false)]
public sealed class PersonNameDataAttribute : DataClassificationAttribute
{
    /// <summary>Initializes a new instance of the <see cref="PersonNameDataAttribute"/> class.</summary>
    public PersonNameDataAttribute()
        : base(PrivacyTaxonomy.PersonName)
    {
    }
}

/// <summary>Marks an email address: <see cref="PrivacyTaxonomy.EmailAddress"/>.</summary>
/// <remarks>On a positional record parameter, write <c>[property: EmailAddressData]</c> so the generated property carries it.</remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.ReturnValue, AllowMultiple = false)]
public sealed class EmailAddressDataAttribute : DataClassificationAttribute
{
    /// <summary>Initializes a new instance of the <see cref="EmailAddressDataAttribute"/> class.</summary>
    public EmailAddressDataAttribute()
        : base(PrivacyTaxonomy.EmailAddress)
    {
    }
}

/// <summary>Marks a phone number: <see cref="PrivacyTaxonomy.PhoneNumber"/>.</summary>
/// <remarks>On a positional record parameter, write <c>[property: PhoneNumberData]</c> so the generated property carries it.</remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.ReturnValue, AllowMultiple = false)]
public sealed class PhoneNumberDataAttribute : DataClassificationAttribute
{
    /// <summary>Initializes a new instance of the <see cref="PhoneNumberDataAttribute"/> class.</summary>
    public PhoneNumberDataAttribute()
        : base(PrivacyTaxonomy.PhoneNumber)
    {
    }
}

/// <summary>Marks a postal or home address: <see cref="PrivacyTaxonomy.PostalAddress"/>.</summary>
/// <remarks>On a positional record parameter, write <c>[property: PostalAddressData]</c> so the generated property carries it.</remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.ReturnValue, AllowMultiple = false)]
public sealed class PostalAddressDataAttribute : DataClassificationAttribute
{
    /// <summary>Initializes a new instance of the <see cref="PostalAddressDataAttribute"/> class.</summary>
    public PostalAddressDataAttribute()
        : base(PrivacyTaxonomy.PostalAddress)
    {
    }
}

/// <summary>Marks a date of birth: <see cref="PrivacyTaxonomy.DateOfBirth"/>.</summary>
/// <remarks>On a positional record parameter, write <c>[property: DateOfBirthData]</c> so the generated property carries it.</remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.ReturnValue, AllowMultiple = false)]
public sealed class DateOfBirthDataAttribute : DataClassificationAttribute
{
    /// <summary>Initializes a new instance of the <see cref="DateOfBirthDataAttribute"/> class.</summary>
    public DateOfBirthDataAttribute()
        : base(PrivacyTaxonomy.DateOfBirth)
    {
    }
}

/// <summary>Marks a government-issued identity number: <see cref="PrivacyTaxonomy.NationalId"/>.</summary>
/// <remarks>On a positional record parameter, write <c>[property: NationalIdData]</c> so the generated property carries it.</remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.ReturnValue, AllowMultiple = false)]
public sealed class NationalIdDataAttribute : DataClassificationAttribute
{
    /// <summary>Initializes a new instance of the <see cref="NationalIdDataAttribute"/> class.</summary>
    public NationalIdDataAttribute()
        : base(PrivacyTaxonomy.NationalId)
    {
    }
}

/// <summary>Marks an identifier that singles out a person online: <see cref="PrivacyTaxonomy.OnlineIdentifier"/>.</summary>
/// <remarks>On a positional record parameter, write <c>[property: OnlineIdentifierData]</c> so the generated property carries it.</remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.ReturnValue, AllowMultiple = false)]
public sealed class OnlineIdentifierDataAttribute : DataClassificationAttribute
{
    /// <summary>Initializes a new instance of the <see cref="OnlineIdentifierDataAttribute"/> class.</summary>
    public OnlineIdentifierDataAttribute()
        : base(PrivacyTaxonomy.OnlineIdentifier)
    {
    }
}

/// <summary>Marks an IP address: <see cref="PrivacyTaxonomy.IpAddress"/>.</summary>
/// <remarks>On a positional record parameter, write <c>[property: IpAddressData]</c> so the generated property carries it.</remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.ReturnValue, AllowMultiple = false)]
public sealed class IpAddressDataAttribute : DataClassificationAttribute
{
    /// <summary>Initializes a new instance of the <see cref="IpAddressDataAttribute"/> class.</summary>
    public IpAddressDataAttribute()
        : base(PrivacyTaxonomy.IpAddress)
    {
    }
}

/// <summary>Marks a precise location: <see cref="PrivacyTaxonomy.Location"/>.</summary>
/// <remarks>On a positional record parameter, write <c>[property: LocationData]</c> so the generated property carries it.</remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.ReturnValue, AllowMultiple = false)]
public sealed class LocationDataAttribute : DataClassificationAttribute
{
    /// <summary>Initializes a new instance of the <see cref="LocationDataAttribute"/> class.</summary>
    public LocationDataAttribute()
        : base(PrivacyTaxonomy.Location)
    {
    }
}

/// <summary>Marks a bank account number or IBAN: <see cref="PrivacyTaxonomy.BankAccount"/>.</summary>
/// <remarks>On a positional record parameter, write <c>[property: BankAccountData]</c> so the generated property carries it.</remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.ReturnValue, AllowMultiple = false)]
public sealed class BankAccountDataAttribute : DataClassificationAttribute
{
    /// <summary>Initializes a new instance of the <see cref="BankAccountDataAttribute"/> class.</summary>
    public BankAccountDataAttribute()
        : base(PrivacyTaxonomy.BankAccount)
    {
    }
}

/// <summary>Marks payment card data: <see cref="PrivacyTaxonomy.PaymentCard"/>.</summary>
/// <remarks>On a positional record parameter, write <c>[property: PaymentCardData]</c> so the generated property carries it.</remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.ReturnValue, AllowMultiple = false)]
public sealed class PaymentCardDataAttribute : DataClassificationAttribute
{
    /// <summary>Initializes a new instance of the <see cref="PaymentCardDataAttribute"/> class.</summary>
    public PaymentCardDataAttribute()
        : base(PrivacyTaxonomy.PaymentCard)
    {
    }
}

/// <summary>Marks financial data about a person: <see cref="PrivacyTaxonomy.Financial"/>.</summary>
/// <remarks>On a positional record parameter, write <c>[property: FinancialData]</c> so the generated property carries it.</remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.ReturnValue, AllowMultiple = false)]
public sealed class FinancialDataAttribute : DataClassificationAttribute
{
    /// <summary>Initializes a new instance of the <see cref="FinancialDataAttribute"/> class.</summary>
    public FinancialDataAttribute()
        : base(PrivacyTaxonomy.Financial)
    {
    }
}

/// <summary>Marks a password, key, token or other secret: <see cref="PrivacyTaxonomy.Credential"/>.</summary>
/// <remarks>On a positional record parameter, write <c>[property: CredentialData]</c> so the generated property carries it.</remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.ReturnValue, AllowMultiple = false)]
public sealed class CredentialDataAttribute : DataClassificationAttribute
{
    /// <summary>Initializes a new instance of the <see cref="CredentialDataAttribute"/> class.</summary>
    public CredentialDataAttribute()
        : base(PrivacyTaxonomy.Credential)
    {
    }
}

/// <summary>Marks special-category health data: <see cref="PrivacyTaxonomy.Health"/>.</summary>
/// <remarks>On a positional record parameter, write <c>[property: HealthData]</c> so the generated property carries it.</remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.ReturnValue, AllowMultiple = false)]
public sealed class HealthDataAttribute : DataClassificationAttribute
{
    /// <summary>Initializes a new instance of the <see cref="HealthDataAttribute"/> class.</summary>
    public HealthDataAttribute()
        : base(PrivacyTaxonomy.Health)
    {
    }
}

/// <summary>Marks special-category genetic data: <see cref="PrivacyTaxonomy.Genetic"/>.</summary>
/// <remarks>On a positional record parameter, write <c>[property: GeneticData]</c> so the generated property carries it.</remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.ReturnValue, AllowMultiple = false)]
public sealed class GeneticDataAttribute : DataClassificationAttribute
{
    /// <summary>Initializes a new instance of the <see cref="GeneticDataAttribute"/> class.</summary>
    public GeneticDataAttribute()
        : base(PrivacyTaxonomy.Genetic)
    {
    }
}

/// <summary>Marks special-category biometric data: <see cref="PrivacyTaxonomy.Biometric"/>.</summary>
/// <remarks>On a positional record parameter, write <c>[property: BiometricData]</c> so the generated property carries it.</remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.ReturnValue, AllowMultiple = false)]
public sealed class BiometricDataAttribute : DataClassificationAttribute
{
    /// <summary>Initializes a new instance of the <see cref="BiometricDataAttribute"/> class.</summary>
    public BiometricDataAttribute()
        : base(PrivacyTaxonomy.Biometric)
    {
    }
}

/// <summary>Marks special-category racial or ethnic origin: <see cref="PrivacyTaxonomy.EthnicOrigin"/>.</summary>
/// <remarks>On a positional record parameter, write <c>[property: EthnicOriginData]</c> so the generated property carries it.</remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.ReturnValue, AllowMultiple = false)]
public sealed class EthnicOriginDataAttribute : DataClassificationAttribute
{
    /// <summary>Initializes a new instance of the <see cref="EthnicOriginDataAttribute"/> class.</summary>
    public EthnicOriginDataAttribute()
        : base(PrivacyTaxonomy.EthnicOrigin)
    {
    }
}

/// <summary>Marks special-category political opinions: <see cref="PrivacyTaxonomy.PoliticalOpinion"/>.</summary>
/// <remarks>On a positional record parameter, write <c>[property: PoliticalOpinionData]</c> so the generated property carries it.</remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.ReturnValue, AllowMultiple = false)]
public sealed class PoliticalOpinionDataAttribute : DataClassificationAttribute
{
    /// <summary>Initializes a new instance of the <see cref="PoliticalOpinionDataAttribute"/> class.</summary>
    public PoliticalOpinionDataAttribute()
        : base(PrivacyTaxonomy.PoliticalOpinion)
    {
    }
}

/// <summary>Marks special-category religious, philosophical or other beliefs: <see cref="PrivacyTaxonomy.Belief"/>.</summary>
/// <remarks>On a positional record parameter, write <c>[property: BeliefData]</c> so the generated property carries it.</remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.ReturnValue, AllowMultiple = false)]
public sealed class BeliefDataAttribute : DataClassificationAttribute
{
    /// <summary>Initializes a new instance of the <see cref="BeliefDataAttribute"/> class.</summary>
    public BeliefDataAttribute()
        : base(PrivacyTaxonomy.Belief)
    {
    }
}

/// <summary>Marks special-category association, foundation or trade union membership: <see cref="PrivacyTaxonomy.Membership"/>.</summary>
/// <remarks>On a positional record parameter, write <c>[property: MembershipData]</c> so the generated property carries it.</remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.ReturnValue, AllowMultiple = false)]
public sealed class MembershipDataAttribute : DataClassificationAttribute
{
    /// <summary>Initializes a new instance of the <see cref="MembershipDataAttribute"/> class.</summary>
    public MembershipDataAttribute()
        : base(PrivacyTaxonomy.Membership)
    {
    }
}

/// <summary>Marks special-category sex life or sexual orientation data: <see cref="PrivacyTaxonomy.SexLife"/>.</summary>
/// <remarks>On a positional record parameter, write <c>[property: SexLifeData]</c> so the generated property carries it.</remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.ReturnValue, AllowMultiple = false)]
public sealed class SexLifeDataAttribute : DataClassificationAttribute
{
    /// <summary>Initializes a new instance of the <see cref="SexLifeDataAttribute"/> class.</summary>
    public SexLifeDataAttribute()
        : base(PrivacyTaxonomy.SexLife)
    {
    }
}

/// <summary>Marks special-category criminal conviction or offence data: <see cref="PrivacyTaxonomy.CriminalRecord"/>.</summary>
/// <remarks>On a positional record parameter, write <c>[property: CriminalRecordData]</c> so the generated property carries it.</remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.ReturnValue, AllowMultiple = false)]
public sealed class CriminalRecordDataAttribute : DataClassificationAttribute
{
    /// <summary>Initializes a new instance of the <see cref="CriminalRecordDataAttribute"/> class.</summary>
    public CriminalRecordDataAttribute()
        : base(PrivacyTaxonomy.CriminalRecord)
    {
    }
}

/// <summary>Marks KVKK special-category appearance and dress data: <see cref="PrivacyTaxonomy.Appearance"/>.</summary>
/// <remarks>On a positional record parameter, write <c>[property: AppearanceData]</c> so the generated property carries it.</remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.ReturnValue, AllowMultiple = false)]
public sealed class AppearanceDataAttribute : DataClassificationAttribute
{
    /// <summary>Initializes a new instance of the <see cref="AppearanceDataAttribute"/> class.</summary>
    public AppearanceDataAttribute()
        : base(PrivacyTaxonomy.Appearance)
    {
    }
}
