using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using SharedKernel.Cryptography.Envelope;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace BillingApi.Infrastructure;

/// <summary>
/// Wraps each tenant's data key with one AES-256-GCM master key from configuration.
/// </summary>
/// <remarks>
/// A LOCAL STAND-IN for a KMS. In production register <c>SharedKernel.Cryptography.KeyVault.Azure</c>'s
/// <c>AddAzureKeyVaultEncryption(...)</c>, which provides the same <see cref="IEnvelopeEncryptionProvider"/> with
/// the master key held in Key Vault. The persistence packages cannot tell the difference.
/// </remarks>
public sealed class LocalMasterKeyEnvelopeProvider(IOptions<LocalMasterKeyOptions> options) : IEnvelopeEncryptionProvider
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    public ValueTask<EnvelopeDataKey> GenerateDataKeyAsync(CancellationToken cancellationToken = default)
    {
        var dataKey = RandomNumberGenerator.GetBytes(32);
        try
        {
            var wrapped = new byte[NonceSize + TagSize + dataKey.Length];
            var span = wrapped.AsSpan();
            RandomNumberGenerator.Fill(span[..NonceSize]);
            using var aes = new AesGcm(MasterKey(), TagSize);
            aes.Encrypt(span[..NonceSize], dataKey, span[(NonceSize + TagSize)..], span.Slice(NonceSize, TagSize));
            return new(new EnvelopeDataKey(dataKey, wrapped, options.Value.KeyId));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
        }
    }

    public ValueTask<Result<byte[]>> UnwrapDataKeyAsync(ReadOnlyMemory<byte> wrappedKey, string masterKeyId, CancellationToken cancellationToken = default)
    {
        var span = wrappedKey.Span;
        if (!string.Equals(masterKeyId, options.Value.KeyId, StringComparison.Ordinal) || span.Length <= NonceSize + TagSize)
            return new(Result<byte[]>.Failure(Error.Unexpected("billing.master_key.unknown", "The data key was wrapped by an unknown master key.")));

        var dataKey = new byte[span.Length - NonceSize - TagSize];
        try
        {
            using var aes = new AesGcm(MasterKey(), TagSize);
            aes.Decrypt(span[..NonceSize], span[(NonceSize + TagSize)..], span.Slice(NonceSize, TagSize), dataKey);
            return new(Result<byte[]>.Success(dataKey));
        }
        catch (AuthenticationTagMismatchException)
        {
            return new(Result<byte[]>.Failure(Error.Unexpected("billing.master_key.unwrap_failed", "The data key could not be unwrapped.")));
        }
    }

    private byte[] MasterKey() => Convert.FromBase64String(options.Value.Material);
}

public sealed class LocalMasterKeyOptions
{
    public const string SectionName = "Billing:MasterKey";

    public string KeyId { get; set; } = "local-master/v1";

    public string Material { get; set; } = string.Empty;
}
