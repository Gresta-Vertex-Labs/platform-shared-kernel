using System.Security.Cryptography;
using System.Text;
using SharedKernel.Cryptography.KeyDerivation;
using SharedKernel.Cryptography.Signing;
using SharedKernel.Cryptography.Symmetric;

namespace SharedKernel.Persistence.EfCore.Encryption.BlindIndex;

/// <summary>Default <see cref="IBlindIndexService"/>: HMAC-SHA256 over a per-purpose (and, optionally, per-tenant) HKDF subkey.</summary>
/// <remarks>
/// The subkey is derived from the CURRENT key (<see cref="ISynchronousEncryptionKeyProvider.GetCurrentKey"/>) under
/// purpose <c>"blindindex:{purpose}"</c> (and, when <c>tenantId</c> is supplied, context the tenant id
/// bytes — so a tenant-isolated encrypted property gets a tenant-isolated blind index too). Because the subkey
/// tracks the CURRENT key, it changes when the root key rotates — see <c>PropertyBuilderEncryptExtensions
/// .WithBlindIndex</c>'s remarks for the resulting rotation story.
/// </remarks>
internal sealed class BlindIndexService : IBlindIndexService
{
    private readonly ISynchronousEncryptionKeyProvider _keyProvider;
    private readonly IHmacSigner _hmacSigner;

    public BlindIndexService(ISynchronousEncryptionKeyProvider keyProvider, IHmacSigner hmacSigner)
    {
        ArgumentNullException.ThrowIfNull(keyProvider);
        ArgumentNullException.ThrowIfNull(hmacSigner);
        _keyProvider = keyProvider;
        _hmacSigner = hmacSigner;
    }

    /// <inheritdoc />
    public string Compute(string purpose, string normalizedValue, Guid? tenantId)
    {
        ArgumentException.ThrowIfNullOrEmpty(purpose);
        ArgumentNullException.ThrowIfNull(normalizedValue);

        var rootKey = _keyProvider.GetCurrentKey();
        var context = tenantId is { } id ? id.ToByteArray() : ReadOnlySpan<byte>.Empty;

        Span<byte> subkey = stackalloc byte[32];
        try
        {
            SubkeyDerivation.DeriveKey(rootKey.Material, $"blindindex:{purpose}", context, subkey);
            var hmac = _hmacSigner.Sign(Encoding.UTF8.GetBytes(normalizedValue), subkey);
            return Convert.ToHexStringLower(hmac);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(subkey);
        }
    }
}
