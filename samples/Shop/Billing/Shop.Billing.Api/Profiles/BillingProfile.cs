using SharedKernel.DataPrivacy.Classification;
using SharedKernel.Domain.Aggregates;
using SharedKernel.Domain.StronglyTypedIds;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Clocks;

namespace Shop.Billing.Api.Profiles;

public sealed record BillingProfileId(Guid Value) : StronglyTypedId<Guid>(Value)
{
    public static BillingProfileId New() => new(Guid.CreateVersion7());
}

/// <summary>How a merchant is invoiced and paid out: one per tenant.</summary>
public sealed class BillingProfile : TenantedAuditableAggregateRoot<BillingProfileId>
{
    private BillingProfile(BillingProfileId id, TenantId tenantId, IClock clock)
        : base(id, tenantId, clock) { }

    private BillingProfile() { }

    public string LegalName { get; private set; } = string.Empty;

    /// <summary>ISO 3166-1 alpha-2; the VAT number is validated for this country.</summary>
    public string Country { get; private set; } = string.Empty;

    public string VatNumber { get; private set; } = string.Empty;

    public string? Bic { get; private set; }

    /// <summary>The payout IBAN as an envelope (<c>EnvelopePayload</c>, Base64Url), never in clear text.</summary>
    [BankAccountData]
    public string IbanEnvelope { get; private set; } = string.Empty;

    public static BillingProfile Create(TenantId tenantId, IClock clock) =>
        new(BillingProfileId.New(), tenantId, clock);

    public void Update(
        string legalName,
        string country,
        string vatNumber,
        string? bic,
        string ibanEnvelope
    )
    {
        LegalName = legalName;
        Country = country;
        VatNumber = vatNumber;
        Bic = bic;
        IbanEnvelope = ibanEnvelope;
    }
}
