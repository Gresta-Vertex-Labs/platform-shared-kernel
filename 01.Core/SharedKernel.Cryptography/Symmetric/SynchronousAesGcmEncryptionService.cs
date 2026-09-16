using System.Security.Cryptography;
using System.Text;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Cryptography.Symmetric;

/// <summary><see cref="ISynchronousSymmetricEncryptionService"/> implemented with <see cref="AesGcm"/>.</summary>
/// <remarks>Thread-safe. Each call looks up its key and creates its own <see cref="AesGcm"/> instance.</remarks>
public sealed class SynchronousAesGcmEncryptionService : ISynchronousSymmetricEncryptionService
{
    private readonly ISynchronousEncryptionKeyProvider _keyProvider;

    /// <summary>Creates the service.</summary>
    /// <param name="keyProvider">Resolves the current and historical keys from memory.</param>
    public SynchronousAesGcmEncryptionService(ISynchronousEncryptionKeyProvider keyProvider)
    {
        ArgumentNullException.ThrowIfNull(keyProvider);
        _keyProvider = keyProvider;
    }

    /// <inheritdoc />
    public EncryptedPayload Encrypt(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> associatedData) =>
        AesGcmCipher.Encrypt(_keyProvider.GetCurrentKey(), plaintext, associatedData);

    /// <inheritdoc />
    public Result<byte[]> Decrypt(EncryptedPayload payload, ReadOnlySpan<byte> associatedData)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return AesGcmCipher.Decrypt(_keyProvider.GetKey(payload.KeyId), payload, associatedData);
    }

    /// <inheritdoc />
    public string EncryptToString(string plaintext, ReadOnlySpan<byte> associatedData)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        byte[] bytes = Encoding.UTF8.GetBytes(plaintext);
        try
        {
            return Encrypt(bytes, associatedData).ToString();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    /// <inheritdoc />
    public Result<string> DecryptToString(string encoded, ReadOnlySpan<byte> associatedData)
    {
        ArgumentNullException.ThrowIfNull(encoded);

        return EncryptedPayload.TryParse(encoded, out EncryptedPayload? payload)
            ? SymmetricText.Decode(Decrypt(payload, associatedData))
            : CryptographyErrors.MalformedPayload();
    }

    /// <inheritdoc />
    public bool IsEncryptedWithCurrentKey(EncryptedPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return string.Equals(_keyProvider.GetCurrentKey().Id, payload.KeyId, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public Result<EncryptedPayload> ReEncrypt(EncryptedPayload payload, ReadOnlySpan<byte> associatedData)
    {
        ArgumentNullException.ThrowIfNull(payload);

        CryptographicKey current = _keyProvider.GetCurrentKey();
        if (string.Equals(current.Id, payload.KeyId, StringComparison.Ordinal))
        {
            return payload;
        }

        Result<byte[]> decrypted = Decrypt(payload, associatedData);
        if (decrypted.IsFailure)
        {
            return decrypted.Error;
        }

        try
        {
            return AesGcmCipher.Encrypt(current, decrypted.Value, associatedData);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(decrypted.Value);
        }
    }
}
