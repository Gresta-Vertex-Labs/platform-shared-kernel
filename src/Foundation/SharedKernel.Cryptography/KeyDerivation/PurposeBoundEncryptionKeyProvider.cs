using System.Security.Cryptography;
using SharedKernel.Cryptography.Symmetric;

namespace SharedKernel.Cryptography.KeyDerivation;

/// <summary>
/// Wraps an <see cref="IEncryptionKeyProvider"/> so that every key it returns is replaced by a subkey derived for one
/// purpose and context.
/// </summary>
/// <remarks>
/// <para>
/// Key ids are unchanged, so rotation of the inner provider carries through. A payload encrypted for one purpose or
/// context fails to decrypt under another, even though both use the same root keys. Root keys must be at least 32
/// bytes.
/// </para>
/// <para>
/// Derivation runs on every lookup and costs a few HMAC computations. Create one instance per purpose and context,
/// for example per tenant, with <see cref="PurposeBoundKeyProviderExtensions.ForPurpose"/>.
/// </para>
/// </remarks>
public sealed class PurposeBoundEncryptionKeyProvider : IEncryptionKeyProvider
{
    private readonly IEncryptionKeyProvider _inner;
    private readonly string _purpose;
    private readonly byte[] _context;

    /// <summary>Creates the provider.</summary>
    /// <param name="inner">The provider of root keys.</param>
    /// <param name="purpose">A fixed name for what the keys are for. Must not be empty.</param>
    /// <param name="context">Optional variable context, such as a tenant id. Copied.</param>
    public PurposeBoundEncryptionKeyProvider(IEncryptionKeyProvider inner, string purpose, ReadOnlySpan<byte> context)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentException.ThrowIfNullOrEmpty(purpose);
        _inner = inner;
        _purpose = purpose;
        _context = context.ToArray();
    }

    /// <inheritdoc />
    public async ValueTask<CryptographicKey> GetCurrentKeyAsync(CancellationToken cancellationToken = default) =>
        PurposeBoundKeys.Derive(await _inner.GetCurrentKeyAsync(cancellationToken).ConfigureAwait(false), _purpose, _context);

    /// <inheritdoc />
    public async ValueTask<CryptographicKey?> GetKeyAsync(string keyId, CancellationToken cancellationToken = default)
    {
        CryptographicKey? key = await _inner.GetKeyAsync(keyId, cancellationToken).ConfigureAwait(false);
        return key is null ? null : PurposeBoundKeys.Derive(key, _purpose, _context);
    }
}

/// <summary>
/// Wraps an <see cref="ISynchronousEncryptionKeyProvider"/> so that every key it returns is replaced by a subkey
/// derived for one purpose and context.
/// </summary>
/// <remarks>The synchronous counterpart of <see cref="PurposeBoundEncryptionKeyProvider"/>, with the same behavior.</remarks>
public sealed class PurposeBoundSynchronousEncryptionKeyProvider : ISynchronousEncryptionKeyProvider
{
    private readonly ISynchronousEncryptionKeyProvider _inner;
    private readonly string _purpose;
    private readonly byte[] _context;

    /// <summary>Creates the provider.</summary>
    /// <param name="inner">The provider of root keys.</param>
    /// <param name="purpose">A fixed name for what the keys are for. Must not be empty.</param>
    /// <param name="context">Optional variable context, such as a tenant id. Copied.</param>
    public PurposeBoundSynchronousEncryptionKeyProvider(
        ISynchronousEncryptionKeyProvider inner,
        string purpose,
        ReadOnlySpan<byte> context)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentException.ThrowIfNullOrEmpty(purpose);
        _inner = inner;
        _purpose = purpose;
        _context = context.ToArray();
    }

    /// <inheritdoc />
    public CryptographicKey GetCurrentKey() => PurposeBoundKeys.Derive(_inner.GetCurrentKey(), _purpose, _context);

    /// <inheritdoc />
    public CryptographicKey? GetKey(string keyId)
    {
        CryptographicKey? key = _inner.GetKey(keyId);
        return key is null ? null : PurposeBoundKeys.Derive(key, _purpose, _context);
    }
}

/// <summary>Creates purpose-bound key providers.</summary>
public static class PurposeBoundKeyProviderExtensions
{
    /// <summary>Returns a provider whose keys are derived from <paramref name="provider"/>'s for one purpose and context.</summary>
    /// <param name="provider">The provider of root keys.</param>
    /// <param name="purpose">A fixed name for what the keys are for.</param>
    /// <param name="context">Optional variable context, such as a tenant id.</param>
    /// <returns>The purpose-bound provider.</returns>
    public static PurposeBoundEncryptionKeyProvider ForPurpose(
        this IEncryptionKeyProvider provider,
        string purpose,
        ReadOnlySpan<byte> context = default) => new(provider, purpose, context);

    /// <summary>
    /// Returns a synchronous provider whose keys are derived from <paramref name="provider"/>'s for one purpose and
    /// context.
    /// </summary>
    /// <param name="provider">The provider of root keys.</param>
    /// <param name="purpose">A fixed name for what the keys are for.</param>
    /// <param name="context">Optional variable context, such as a tenant id.</param>
    /// <returns>The purpose-bound provider.</returns>
    public static PurposeBoundSynchronousEncryptionKeyProvider ForPurposeSynchronous(
        this ISynchronousEncryptionKeyProvider provider,
        string purpose,
        ReadOnlySpan<byte> context = default) => new(provider, purpose, context);
}

internal static class PurposeBoundKeys
{
    public static CryptographicKey Derive(CryptographicKey rootKey, string purpose, ReadOnlySpan<byte> context)
    {
        Span<byte> subkey = stackalloc byte[AesGcmCipher.KeySize];
        try
        {
            SubkeyDerivation.DeriveKey(rootKey.Material, purpose, context, subkey);
            return new CryptographicKey(rootKey.Id, subkey);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(subkey);
        }
    }
}
