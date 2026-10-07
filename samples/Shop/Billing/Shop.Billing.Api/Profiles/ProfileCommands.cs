using System.Text;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Application.Authorization;
using SharedKernel.Application.Messaging;
using SharedKernel.Cryptography.Envelope;
using SharedKernel.DataPrivacy.Masking;
using SharedKernel.Execution.Context;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Validation;
using SharedKernel.Validation.FluentValidation;
using Shop.Billing.Api.Payments;
using Shop.Billing.Api.Persistence;

namespace Shop.Billing.Api.Profiles;

/// <summary>A billing profile as a merchant sees it: the IBAN only masked.</summary>
public sealed record BillingProfileView(
    string LegalName,
    string Country,
    string VatNumber,
    string? Bic,
    string IbanMasked
);

/// <summary>
/// Sets the merchant's billing profile. The IBAN, BIC, country and VAT number are validated by 01.Core Validation's
/// FluentValidation rules; the IBAN is stored envelope-encrypted under the Key Vault master key.
/// </summary>
[RequirePermission(BillingPermissions.Manage)]
public sealed record SaveBillingProfileCommand(
    string LegalName,
    string Country,
    string VatNumber,
    string Iban,
    string? Bic
) : ICommand;

public sealed class SaveBillingProfileValidator : AbstractValidator<SaveBillingProfileCommand>
{
    public SaveBillingProfileValidator()
    {
        RuleFor(c => c.LegalName).NotEmpty().MaximumLength(200);
        RuleFor(c => c.Country).MustBeValidCountryCode();
        RuleFor(c => c.VatNumber).MustBeValidVatNumber(c => c.Country);
        RuleFor(c => c.Iban).MustBeValidIban();
        RuleFor(c => c.Bic!).MustBeValidBic().When(c => c.Bic is not null);
    }
}

public sealed class SaveBillingProfileHandler(
    IRequestContext caller,
    BillingDbContext db,
    IEnvelopeEncryptionService envelopes,
    IClock clock
) : ICommandHandler<SaveBillingProfileCommand>
{
    public async Task<Result> Handle(SaveBillingProfileCommand command, CancellationToken ct)
    {
        var tenant = Caller.Tenant(caller);
        if (tenant.IsFailure)
        {
            return Result.Failure(tenant.Error);
        }

        // The validator accepted them; Create normalizes (upper case, no spaces).
        var iban = Iban.Create(command.Iban).Value;
        var vat = VatNumber
            .Create(CountryCode.Create(command.Country).Value, command.VatNumber)
            .Value;

        var envelope = await envelopes.EncryptAsync(
            Encoding.UTF8.GetBytes(iban.Value),
            ProfileEncryption.AssociatedData(tenant.Value.Value),
            ct
        );

        var profile = await db.Profiles.FirstOrDefaultAsync(ct);
        if (profile is null)
        {
            profile = BillingProfile.Create(tenant.Value, clock);
            db.Profiles.Add(profile);
        }

        profile.Update(
            command.LegalName.Trim(),
            command.Country.ToUpperInvariant(),
            vat.Value,
            command.Bic?.ToUpperInvariant(),
            envelope.ToString()
        );
        return Result.Success();
    }
}

[RequirePermission(BillingPermissions.Manage)]
public sealed record GetBillingProfileQuery : IQuery<BillingProfileView>;

public sealed class GetBillingProfileHandler(
    IRequestContext caller,
    BillingDbContext db,
    IEnvelopeEncryptionService envelopes
) : IQueryHandler<GetBillingProfileQuery, BillingProfileView>
{
    public async Task<Result<BillingProfileView>> Handle(
        GetBillingProfileQuery query,
        CancellationToken ct
    )
    {
        var tenant = Caller.Tenant(caller);
        if (tenant.IsFailure)
        {
            return Result<BillingProfileView>.Failure(tenant.Error);
        }

        var profile = await db.Profiles.AsNoTracking().FirstOrDefaultAsync(ct);
        if (profile is null)
        {
            return Result<BillingProfileView>.Failure(
                Error.NotFound("billing.profile.not_found", "No billing profile yet.")
            );
        }

        if (!EnvelopePayload.TryParse(profile.IbanEnvelope, out var payload))
        {
            return Result<BillingProfileView>.Failure(
                Error.Unexpected("billing.profile.corrupt", "The stored IBAN is not an envelope.")
            );
        }

        // Unwrapping the data key is a Key Vault call: the stored value is useless without the vault.
        var iban = await envelopes.DecryptAsync(
            payload,
            ProfileEncryption.AssociatedData(tenant.Value.Value),
            ct
        );
        if (iban.IsFailure)
        {
            return Result<BillingProfileView>.Failure(iban.Error);
        }

        return Result<BillingProfileView>.Success(
            new BillingProfileView(
                profile.LegalName,
                profile.Country,
                profile.VatNumber,
                profile.Bic,
                PiiMasking.Iban(Encoding.UTF8.GetString(iban.Value))
            )
        );
    }
}

internal static class ProfileEncryption
{
    /// <summary>Binds the ciphertext to its tenant: another tenant's envelope does not decrypt here.</summary>
    public static byte[] AssociatedData(Guid tenant) =>
        Encoding.UTF8.GetBytes($"billing.profile.iban:{tenant:D}");
}
