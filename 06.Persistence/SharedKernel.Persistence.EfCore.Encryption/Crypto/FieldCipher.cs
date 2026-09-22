using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using SharedKernel.Cryptography.KeyDerivation;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Persistence.EfCore.Encryption.TenantKeys;

namespace SharedKernel.Persistence.EfCore.Encryption.Crypto;

/// <summary>The outcome of decrypting one stored value.</summary>
internal enum DecryptOutcome
{
    Success,
    UnknownKey,
    TenantKeyShredded,
    AuthenticationFailed,
}

/// <summary>
/// AES-256-GCM over per-purpose keys: every encrypted column gets its own key, derived with HKDF-SHA256 from the
/// root key (or the tenant's data key) and the column's purpose.
/// </summary>
/// <remarks>
/// <para>
/// Separate keys per purpose keep one column's ciphertext useless under another column's key and spread the random
/// 96-bit nonces over many keys instead of one. Derived keys are cached per key id and purpose; a key id always
/// names the same material, so the cache never goes stale.
/// </para>
/// <para>
/// The stored form is <see cref="EncryptedPayload"/>'s base64url text; its key id is the root key's id, or
/// <c>skt:{tenant}</c> for a tenant data key, so a value always says which key opens it.
/// </para>
/// </remarks>
internal sealed class FieldCipher
{
    private readonly ConcurrentDictionary<(string KeyId, string Purpose), byte[]> _rootDerived = new();

    /// <summary>Encrypts <paramref name="plaintext"/> under a root key.</summary>
    public string EncryptWithRootKey(CryptographicKey rootKey, string purpose, ReadOnlySpan<byte> associatedData, string plaintext) =>
        Seal(rootKey.Id, DeriveRoot(rootKey, purpose), associatedData, plaintext);

    /// <summary>Encrypts <paramref name="plaintext"/> under a tenant's data key.</summary>
    public static string EncryptWithTenantKey(TenantKeyEntry tenantKey, string purpose, ReadOnlySpan<byte> associatedData, string plaintext) =>
        Seal(tenantKey.KeyId, tenantKey.Derive(purpose), associatedData, plaintext);

    /// <summary>Decrypts one payload.</summary>
    /// <param name="payload">The parsed payload.</param>
    /// <param name="purpose">The column's purpose.</param>
    /// <param name="associatedData">The row's associated data.</param>
    /// <param name="rowTenantId">The row's tenant, for a tenanted entity.</param>
    /// <param name="rootKeys">Looks a root key up by id; <see langword="null"/> when unknown.</param>
    /// <param name="tenantKeys">Looks a tenant key up; <see langword="null"/> when the tenant has none.</param>
    /// <param name="plaintext">The decrypted text on success.</param>
    public DecryptOutcome TryDecrypt(
        EncryptedPayload payload,
        string purpose,
        ReadOnlySpan<byte> associatedData,
        Guid? rowTenantId,
        Func<string, CryptographicKey?> rootKeys,
        Func<Guid, TenantKeyEntry?> tenantKeys,
        out string? plaintext)
    {
        plaintext = null;
        byte[] key;

        if (TenantKeyIds.TryParse(payload.KeyId, out var keyTenant))
        {
            if (rowTenantId != keyTenant)
                return DecryptOutcome.AuthenticationFailed;

            var entry = tenantKeys(keyTenant);
            if (entry is null)
                return DecryptOutcome.UnknownKey;
            if (entry.IsShredded)
                return DecryptOutcome.TenantKeyShredded;

            try
            {
                key = entry.Derive(purpose);
            }
            catch (TenantKeyShreddedException)
            {
                return DecryptOutcome.TenantKeyShredded;
            }
        }
        else
        {
            if (rootKeys(payload.KeyId) is not { } rootKey)
                return DecryptOutcome.UnknownKey;

            key = DeriveRoot(rootKey, purpose);
        }

        var buffer = new byte[payload.Ciphertext.Length];
        try
        {
            using (var aes = new AesGcm(key, EncryptedPayload.TagSize))
                aes.Decrypt(payload.Nonce, payload.Ciphertext, payload.Tag, buffer, associatedData);

            plaintext = Encoding.UTF8.GetString(buffer);
            return DecryptOutcome.Success;
        }
        catch (AuthenticationTagMismatchException)
        {
            return DecryptOutcome.AuthenticationFailed;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
        }
    }

    private byte[] DeriveRoot(CryptographicKey rootKey, string purpose)
    {
        if (_rootDerived.TryGetValue((rootKey.Id, purpose), out var cached))
            return cached;

        var derived = SubkeyDerivation.DeriveKey(rootKey.Material, "sk.persistence.field-encryption", Encoding.UTF8.GetBytes(purpose));
        return _rootDerived.GetOrAdd((rootKey.Id, purpose), derived);
    }

    private static string Seal(string keyId, byte[] key, ReadOnlySpan<byte> associatedData, string plaintext)
    {
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        try
        {
            Span<byte> nonce = stackalloc byte[EncryptedPayload.NonceSize];
            Span<byte> tag = stackalloc byte[EncryptedPayload.TagSize];
            var ciphertext = new byte[plaintextBytes.Length];
            RandomNumberGenerator.Fill(nonce);
            using (var aes = new AesGcm(key, EncryptedPayload.TagSize))
                aes.Encrypt(nonce, plaintextBytes, ciphertext, tag, associatedData);

            return new EncryptedPayload(keyId, nonce, ciphertext, tag).ToString();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintextBytes);
        }
    }
}
