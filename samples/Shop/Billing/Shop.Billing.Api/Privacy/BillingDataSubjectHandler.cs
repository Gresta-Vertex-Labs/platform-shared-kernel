using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using SharedKernel.DataPrivacy.DataSubjectRequests;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Results;
using Shop.Billing.Api.Persistence;

namespace Shop.Billing.Api.Privacy;

/// <summary>What Billing exports about a customer: their payments.</summary>
public sealed record CustomerPaymentRecord(
    Guid PaymentId,
    Guid OrderId,
    decimal Amount,
    string Currency,
    string Status
);

[JsonSerializable(typeof(CustomerPaymentRecord))]
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
internal sealed partial class PrivacyJsonContext : JsonSerializerContext;

/// <summary>
/// GDPR access and erasure for Billing (01.Core DataPrivacy). The subject is the customer's email, inside the caller's
/// tenant (row-level security scopes the query). Erasure anonymizes: the email is removed and the payments stay,
/// because accounting law requires the invoices (which carry no personal data) to be kept.
/// </summary>
public sealed class BillingDataSubjectHandler(BillingDbContext db, IClock clock)
    : IDataSubjectRequestHandler
{
    public const string Source = "billing";

    public async Task<Result<DataSubjectExport>> ExportAsync(
        DataSubjectRequest request,
        CancellationToken cancellationToken = default
    )
    {
        var payments = await db
            .Payments.AsNoTracking()
            .Where(p => p.CustomerEmail == request.SubjectId)
            .ToListAsync(cancellationToken);

        var records = payments
            .Select(p =>
                DataSubjectRecord.Create(
                    "payment",
                    new CustomerPaymentRecord(
                        p.Id.Value,
                        p.OrderId,
                        p.Amount.Amount,
                        p.Amount.Currency.Code,
                        p.Status.ToString()
                    ),
                    PrivacyJsonContext.Default.CustomerPaymentRecord
                )
            )
            .ToList();
        return Result<DataSubjectExport>.Success(
            new DataSubjectExport(request, Source, clock.UtcNow, records)
        );
    }

    public async Task<Result<DataSubjectErasureReceipt>> EraseAsync(
        DataSubjectRequest request,
        CancellationToken cancellationToken = default
    )
    {
        var payments = await db
            .Payments.Where(p => p.CustomerEmail == request.SubjectId)
            .ToListAsync(cancellationToken);
        foreach (var payment in payments)
        {
            payment.ErasePersonalData();
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result<DataSubjectErasureReceipt>.Success(
            new DataSubjectErasureReceipt(
                request,
                Source,
                clock.UtcNow,
                ErasedRecords: 0,
                AnonymizedRecords: payments.Count,
                Retained: []
            )
        );
    }
}
