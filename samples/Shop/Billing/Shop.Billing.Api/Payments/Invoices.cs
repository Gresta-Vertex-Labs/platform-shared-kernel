using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using SharedKernel.Compression;
using SharedKernel.Configuration;
using SharedKernel.Core.Extensions;
using SharedKernel.Cryptography.Signing;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using Shop.Billing.Api.Profiles;

namespace Shop.Billing.Api.Payments;

/// <summary>Billing's own settings (<c>Billing</c>).</summary>
public sealed class BillingOptions : ISectionBoundOptions
{
    public static string SectionName => "Billing";

    /// <summary>The signing key (a key id of <c>SharedKernel:Cryptography:KeyVault:Azure:Signing:Keys</c>).</summary>
    [Required]
    public string InvoiceSigningKeyId { get; set; } = string.Empty;
}

/// <summary>What an invoice says.</summary>
public sealed record InvoiceDocument(
    string InvoiceNumber,
    Guid PaymentId,
    Guid OrderId,
    decimal Amount,
    string Currency,
    DateTimeOffset IssuedAt,
    string? SellerLegalName,
    string? SellerVatNumber
);

/// <summary>An invoice as Billing hands it out: the document, and whether its signature still verifies.</summary>
public sealed record InvoiceView(
    InvoiceDocument Document,
    string Signature,
    string SigningKeyId,
    bool SignatureVerified,
    int StoredBytes
);

[JsonSerializable(typeof(InvoiceDocument))]
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
internal sealed partial class InvoiceJsonContext : JsonSerializerContext;

/// <summary>
/// Issues invoices: the document as JSON, compressed (01.Core Compression), then signed with an RSA key that never
/// leaves Key Vault (01.Core Cryptography.KeyVault.Azure). The signature covers the stored bytes, so reading an
/// invoice back verifies exactly what the database holds.
/// </summary>
public sealed class InvoiceIssuer(
    IPayloadCompressor compressor,
    IAsymmetricSignatureService signatures,
    IOptions<BillingOptions> options,
    IClock clock
)
{
    public async Task<Result> IssueAsync(
        Payment payment,
        BillingProfile? seller,
        CancellationToken ct
    )
    {
        var document = new InvoiceDocument(
            $"INV-{payment.Id.Value:N}"[..16].ToUpperInvariant(),
            payment.Id.Value,
            payment.OrderId,
            payment.Amount.Amount,
            payment.Amount.Currency.Code,
            clock.UtcNow,
            seller?.LegalName,
            seller?.VatNumber
        );
        byte[] stored = compressor.Compress(
            JsonSerializer.SerializeToUtf8Bytes(
                document,
                InvoiceJsonContext.Default.InvoiceDocument
            )
        );

        string keyId = options.Value.InvoiceSigningKeyId;
        var signed = await ResultTry.TryAsync(
            async token => await signatures.SignAsync(stored, keyId, token),
            exception => Error.Unavailable("billing.invoice.signing_failed", exception.Message),
            ct
        );
        if (signed.IsFailure)
        {
            return Result.Failure(signed.Error);
        }

        payment.AttachInvoice(stored, signed.Value, keyId);
        return Result.Success();
    }

    public async Task<Result<InvoiceView>> ReadAsync(Payment payment, CancellationToken ct)
    {
        bool verified = await signatures.VerifyAsync(
            payment.Invoice,
            payment.InvoiceSignature,
            payment.InvoiceSigningKeyId,
            ct
        );
        var json = compressor.Decompress(payment.Invoice);
        if (json.IsFailure)
        {
            return Result<InvoiceView>.Failure(json.Error);
        }

        var document = JsonSerializer.Deserialize(
            json.Value,
            InvoiceJsonContext.Default.InvoiceDocument
        )!;
        return Result<InvoiceView>.Success(
            new InvoiceView(
                document,
                Convert.ToBase64String(payment.InvoiceSignature),
                payment.InvoiceSigningKeyId,
                verified,
                payment.Invoice.Length
            )
        );
    }
}
