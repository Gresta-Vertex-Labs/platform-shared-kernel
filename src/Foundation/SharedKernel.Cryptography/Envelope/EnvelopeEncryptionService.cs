using System.Security.Cryptography;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Cryptography.Envelope;

/// <summary><see cref="IEnvelopeEncryptionService"/> using AES-256-GCM data keys.</summary>
/// <remarks>Thread-safe.</remarks>
public sealed class EnvelopeEncryptionService : IEnvelopeEncryptionService
{
    private readonly IEnvelopeEncryptionProvider _provider;

    /// <summary>Creates the service.</summary>
    /// <param name="provider">Generates and unwraps data keys.</param>
    public EnvelopeEncryptionService(IEnvelopeEncryptionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _provider = provider;
    }

    /// <inheritdoc />
    public async ValueTask<EnvelopePayload> EncryptAsync(
        ReadOnlyMemory<byte> plaintext,
        ReadOnlyMemory<byte> associatedData,
        CancellationToken cancellationToken = default)
    {
        using EnvelopeDataKey dataKey = await _provider.GenerateDataKeyAsync(cancellationToken).ConfigureAwait(false);
        EnsureKeySize(dataKey.PlaintextKey.Length);

        byte[] aad = BuildAssociatedData(dataKey.MasterKeyId, dataKey.WrappedKey, associatedData.Span);
        Span<byte> nonce = stackalloc byte[EncryptedPayload.NonceSize];
        Span<byte> tag = stackalloc byte[EncryptedPayload.TagSize];
        byte[] ciphertext = new byte[plaintext.Length];
        RandomNumberGenerator.Fill(nonce);

        using (var aes = new AesGcm(dataKey.PlaintextKey, EncryptedPayload.TagSize))
        {
            aes.Encrypt(nonce, plaintext.Span, ciphertext, tag, aad);
        }

        return new EnvelopePayload(dataKey.MasterKeyId, dataKey.WrappedKey, nonce, ciphertext, tag);
    }

    /// <inheritdoc />
    public async ValueTask<Result<byte[]>> DecryptAsync(
        EnvelopePayload payload,
        ReadOnlyMemory<byte> associatedData,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);

        Result<byte[]> unwrapped = await _provider
            .UnwrapDataKeyAsync(payload.WrappedKey.ToArray(), payload.MasterKeyId, cancellationToken)
            .ConfigureAwait(false);

        if (unwrapped.IsFailure)
        {
            return Error.Validation(CryptographyErrorCodes.DataKeyUnwrapFailed, "The payload's data key could not be unwrapped.");
        }

        byte[] dataKey = unwrapped.Value;
        try
        {
            if (dataKey.Length != AesGcmCipher.KeySize)
            {
                return Error.Validation(CryptographyErrorCodes.DataKeyUnwrapFailed, "The payload's data key has the wrong length.");
            }

            byte[] aad = BuildAssociatedData(payload.MasterKeyId, payload.WrappedKey, associatedData.Span);
            byte[] plaintext = new byte[payload.Ciphertext.Length];
            try
            {
                using var aes = new AesGcm(dataKey, EncryptedPayload.TagSize);
                aes.Decrypt(payload.Nonce, payload.Ciphertext, payload.Tag, plaintext, aad);
                return plaintext;
            }
            catch (AuthenticationTagMismatchException)
            {
                return CryptographyErrors.DecryptionFailed();
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
        }
    }

    private static byte[] BuildAssociatedData(string masterKeyId, ReadOnlySpan<byte> wrappedKey, ReadOnlySpan<byte> callerData)
    {
        byte[] header = EnvelopePayload.WriteHeader(masterKeyId, wrappedKey);
        byte[] aad = new byte[header.Length + callerData.Length];
        header.CopyTo(aad, 0);
        callerData.CopyTo(aad.AsSpan(header.Length));
        return aad;
    }

    private static void EnsureKeySize(int length)
    {
        if (length != AesGcmCipher.KeySize)
        {
            throw new CryptographicException(
                $"The envelope provider generated a {length}-byte data key; AES-256-GCM requires {AesGcmCipher.KeySize} bytes.");
        }
    }
}
