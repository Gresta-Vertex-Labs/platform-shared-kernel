using System.Security.Cryptography;
using System.Text;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Cryptography.Symmetric;

/// <summary><see cref="ISymmetricEncryptionService"/> implemented with <see cref="AesGcm"/>.</summary>
/// <remarks>Thread-safe. Each call looks up its key and creates its own <see cref="AesGcm"/> instance.</remarks>
public sealed class AesGcmEncryptionService : ISymmetricEncryptionService
{
    private readonly IEncryptionKeyProvider _keyProvider;

    /// <summary>Creates the service.</summary>
    /// <param name="keyProvider">Resolves the current and historical keys.</param>
    public AesGcmEncryptionService(IEncryptionKeyProvider keyProvider)
    {
        ArgumentNullException.ThrowIfNull(keyProvider);
        _keyProvider = keyProvider;
    }

    /// <inheritdoc />
    public async ValueTask<EncryptedPayload> EncryptAsync(
        ReadOnlyMemory<byte> plaintext,
        ReadOnlyMemory<byte> associatedData,
        CancellationToken cancellationToken = default)
    {
        CryptographicKey key = await _keyProvider.GetCurrentKeyAsync(cancellationToken).ConfigureAwait(false);
        return AesGcmCipher.Encrypt(key, plaintext.Span, associatedData.Span);
    }

    /// <inheritdoc />
    public async ValueTask<Result<byte[]>> DecryptAsync(
        EncryptedPayload payload,
        ReadOnlyMemory<byte> associatedData,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);

        CryptographicKey? key = await _keyProvider.GetKeyAsync(payload.KeyId, cancellationToken).ConfigureAwait(false);
        return AesGcmCipher.Decrypt(key, payload, associatedData.Span);
    }

    /// <inheritdoc />
    public async ValueTask<string> EncryptToStringAsync(
        string plaintext,
        ReadOnlyMemory<byte> associatedData,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        CryptographicKey key = await _keyProvider.GetCurrentKeyAsync(cancellationToken).ConfigureAwait(false);
        byte[] bytes = Encoding.UTF8.GetBytes(plaintext);
        try
        {
            return AesGcmCipher.Encrypt(key, bytes, associatedData.Span).ToString();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    /// <inheritdoc />
    public async ValueTask<Result<string>> DecryptToStringAsync(
        string encoded,
        ReadOnlyMemory<byte> associatedData,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(encoded);

        if (!EncryptedPayload.TryParse(encoded, out EncryptedPayload? payload))
        {
            return CryptographyErrors.MalformedPayload();
        }

        Result<byte[]> decrypted = await DecryptAsync(payload, associatedData, cancellationToken).ConfigureAwait(false);
        return SymmetricText.Decode(decrypted);
    }

    /// <inheritdoc />
    public async ValueTask<bool> IsEncryptedWithCurrentKeyAsync(
        EncryptedPayload payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);

        CryptographicKey current = await _keyProvider.GetCurrentKeyAsync(cancellationToken).ConfigureAwait(false);
        return string.Equals(current.Id, payload.KeyId, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public async ValueTask<Result<EncryptedPayload>> ReEncryptAsync(
        EncryptedPayload payload,
        ReadOnlyMemory<byte> associatedData,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);

        CryptographicKey current = await _keyProvider.GetCurrentKeyAsync(cancellationToken).ConfigureAwait(false);
        if (string.Equals(current.Id, payload.KeyId, StringComparison.Ordinal))
        {
            return payload;
        }

        Result<byte[]> decrypted = await DecryptAsync(payload, associatedData, cancellationToken).ConfigureAwait(false);
        if (decrypted.IsFailure)
        {
            return decrypted.Error;
        }

        try
        {
            return AesGcmCipher.Encrypt(current, decrypted.Value, associatedData.Span);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(decrypted.Value);
        }
    }
}

/// <summary>UTF-8 decoding of decrypted text, shared by both services.</summary>
internal static class SymmetricText
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static Result<string> Decode(Result<byte[]> decrypted)
    {
        if (decrypted.IsFailure)
        {
            return decrypted.Error;
        }

        try
        {
            return StrictUtf8.GetString(decrypted.Value);
        }
        catch (DecoderFallbackException)
        {
            return CryptographyErrors.MalformedPayload();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(decrypted.Value);
        }
    }
}
