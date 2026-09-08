using System.Collections.Concurrent;
using System.Security.Cryptography;
using SharedKernel.Cryptography.Symmetric;

namespace SharedKernel.Testing.Cryptography;

/// <summary>
/// In-memory test double for <see cref="IEncryptionKeyProvider"/> that plausibly represents a
/// KMS/remote-style provider (the shape a future <c>01.Core</c>
/// <c>AzureKeyVaultEncryptionKeyProvider</c>-style implementation would take) — genuinely
/// asynchronous, never implementing <see cref="ISynchronousEncryptionKeyProvider"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>(P-502/WO-081)</b> Both members insert a real <see cref="Task.Yield"/> before resolving key
/// material, so a caller genuinely yields the calling thread rather than observing an
/// already-completed <see cref="ValueTask{TResult}"/> — the opposite of
/// <see cref="FakeEncryptionKeyProvider"/>'s honest "genuinely synchronous" claim. This fake
/// deliberately does NOT implement <see cref="ISynchronousEncryptionKeyProvider"/>: a KMS/HSM-backed
/// provider must never make that claim (see that marker's own type-level remarks), and this fake
/// exists specifically to exercise the P-492 gate's "unmarked provider" branch —
/// <c>AesGcmEncryptionService</c>'s retained synchronous <c>Encrypt</c>/<c>Decrypt</c>/
/// <c>EncryptToString</c>/<c>DecryptToString</c> members throw <see cref="NotSupportedException"/>
/// when wired to this provider, exactly as they would against a real remote KMS.
/// </para>
/// <para>
/// <b>TEST-ONLY — NEVER PRODUCTION-SAFE.</b> Key material lives only in an in-process
/// <see cref="ConcurrentDictionary{TKey,TValue}"/> with zero persistence, rotation policy, or real
/// Key Vault/HSM-backed hardening — wiring this into a production DI container would silently
/// discard every key on process restart, and would not even provide the network-latency realism
/// this fake simulates.
/// </para>
/// <para>
/// Deliberately NOT registered by <see cref="FakeCryptographyServiceCollectionExtensions.AddFakeCryptography"/>
/// — a test wanting the gated/async-only path constructs this type directly, mirroring how a test
/// opts into <see cref="FakeSymmetricEncryptionService.SimulateDecryptFailure"/> or
/// <see cref="FakeEnvelopeEncryptionProvider.SimulateFailure"/> on other fakes in this bundle today.
/// </para>
/// </remarks>
public sealed class FakeRemoteEncryptionKeyProvider : IEncryptionKeyProvider
{
    private readonly ConcurrentDictionary<string, CryptographicKey> _keys = new();
    private readonly Lock _gate = new();
    private string _currentKeyId;

    /// <summary>
    /// Initialises a new <see cref="FakeRemoteEncryptionKeyProvider"/>, seeding one key
    /// immediately.
    /// </summary>
    /// <param name="currentKeyId">The identifier of the initially-seeded, current key. Defaults to <c>"v1"</c>.</param>
    public FakeRemoteEncryptionKeyProvider(string currentKeyId = "v1")
    {
        _currentKeyId = currentKeyId;
        AddKey(currentKeyId);
    }

    /// <summary>Generates and registers a fresh 32-byte key under <paramref name="keyId"/>.</summary>
    /// <param name="keyId">The key version identifier.</param>
    /// <returns>The newly generated <see cref="CryptographicKey"/>.</returns>
    public CryptographicKey AddKey(string keyId)
    {
        byte[] material = new byte[32];
        RandomNumberGenerator.Fill(material);
        var key = new CryptographicKey(keyId, material);
        _keys[keyId] = key;
        return key;
    }

    /// <summary>Designates <paramref name="keyId"/> as the key returned by <see cref="GetCurrentKeyAsync(CancellationToken)"/>.</summary>
    /// <param name="keyId">The key version identifier to make current.</param>
    public void SetCurrentKey(string keyId)
    {
        lock (_gate)
        {
            _currentKeyId = keyId;
        }
    }

    /// <summary>Removes the key registered under <paramref name="keyId"/>, simulating key retirement.</summary>
    /// <param name="keyId">The key version identifier to remove.</param>
    public void RemoveKey(string keyId) => _keys.TryRemove(keyId, out _);

    /// <inheritdoc />
    /// <remarks>
    /// Genuinely yields via <see cref="Task.Yield"/> before resolving — never an already-completed
    /// <see cref="ValueTask{TResult}"/>. See class remarks.
    /// </remarks>
    public async ValueTask<CryptographicKey> GetCurrentKeyAsync(CancellationToken ct = default)
    {
        await Task.Yield();

        string currentKeyId;
        lock (_gate)
        {
            currentKeyId = _currentKeyId;
        }

        return _keys[currentKeyId];
    }

    /// <inheritdoc />
    /// <remarks>
    /// Genuinely yields via <see cref="Task.Yield"/> before resolving — see
    /// <see cref="GetCurrentKeyAsync(CancellationToken)"/>'s remarks.
    /// </remarks>
    public async ValueTask<CryptographicKey?> GetKeyAsync(string keyId, CancellationToken ct = default)
    {
        await Task.Yield();

        return _keys.GetValueOrDefault(keyId);
    }
}
