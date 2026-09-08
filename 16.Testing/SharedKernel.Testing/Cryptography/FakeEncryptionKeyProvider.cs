using System.Collections.Concurrent;
using System.Security.Cryptography;
using SharedKernel.Cryptography.Symmetric;

namespace SharedKernel.Testing.Cryptography;

/// <summary>
/// In-memory test double for <see cref="IEncryptionKeyProvider"/> — supports multiple key versions,
/// one designated as current.
/// </summary>
/// <remarks>
/// <para>
/// Promoted from <c>SharedKernel.Cryptography.Tests</c>' internal <c>InMemoryEncryptionKeyProvider</c>
/// test double into this package's public, shared surface (zero behavioral drift; hardened here for
/// thread safety since fakes in this package may be shared across parallel xUnit collections).
/// Multi-key-version support lets a test exercise a key-rotation "decrypt an older payload" scenario
/// exactly like a real Key Vault-backed provider would.
/// </para>
/// <para>
/// <b>TEST-ONLY — NEVER PRODUCTION-SAFE.</b> Key material lives only in an in-process
/// <see cref="ConcurrentDictionary{TKey,TValue}"/> with zero persistence, rotation policy, or
/// Key Vault/HSM-backed hardening — wiring this into a production DI container would silently
/// discard every key on process restart.
/// </para>
/// <para>
/// <b>BREAKING CHANGE (P-450/WO-068):</b> migrated onto <c>IEncryptionKeyProvider</c>'s async
/// contract (P-446/WO-068) — the former synchronous <c>GetCurrentKey()</c>/<c>GetKey(string)</c>
/// members are replaced outright by <see cref="GetCurrentKeyAsync(CancellationToken)"/>/
/// <see cref="GetKeyAsync(string, CancellationToken)"/>, mirroring the real interface's own
/// breaking shape. <see cref="AddKey(string)"/>, <see cref="SetCurrentKey(string)"/>, and
/// <see cref="RemoveKey(string)"/> are this fake's own additive test-control surface — not part
/// of the interface — and are unchanged.
/// </para>
/// <para>
/// <b>(P-502/WO-081)</b> Now additionally implements <see cref="ISynchronousEncryptionKeyProvider"/>
/// — an HONEST claim, not an inferred one: both members below perform no real I/O and always
/// complete via an already-completed <see cref="ValueTask{TResult}"/>, exactly the safety property
/// that marker requires. This lets a test compose this fake with the REAL <c>01.Core</c>
/// <c>AesGcmEncryptionService</c> and exercise its retained synchronous
/// <c>Encrypt</c>/<c>Decrypt</c>/<c>EncryptToString</c>/<c>DecryptToString</c> members without
/// hitting the P-492 <see cref="NotSupportedException"/> gate. Contrast with
/// <see cref="FakeRemoteEncryptionKeyProvider"/>, which deliberately never implements this marker.
/// </para>
/// </remarks>
public sealed class FakeEncryptionKeyProvider : ISynchronousEncryptionKeyProvider
{
    private readonly ConcurrentDictionary<string, CryptographicKey> _keys = new();
    private readonly Lock _gate = new();
    private string _currentKeyId;

    /// <summary>
    /// Initialises a new <see cref="FakeEncryptionKeyProvider"/>, seeding one key immediately.
    /// </summary>
    /// <param name="currentKeyId">The identifier of the initially-seeded, current key. Defaults to <c>"v1"</c>.</param>
    public FakeEncryptionKeyProvider(string currentKeyId = "v1")
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
    /// Completes synchronously via an already-completed <see cref="ValueTask{TResult}"/> — this
    /// fake performs no real I/O, mirroring a configuration-based production implementation of
    /// <see cref="IEncryptionKeyProvider"/>.
    /// </remarks>
    public ValueTask<CryptographicKey> GetCurrentKeyAsync(CancellationToken ct = default)
    {
        string currentKeyId;
        lock (_gate)
        {
            currentKeyId = _currentKeyId;
        }

        return new ValueTask<CryptographicKey>(_keys[currentKeyId]);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Completes synchronously via an already-completed <see cref="ValueTask{TResult}"/> — see
    /// <see cref="GetCurrentKeyAsync(CancellationToken)"/>'s remarks.
    /// </remarks>
    public ValueTask<CryptographicKey?> GetKeyAsync(string keyId, CancellationToken ct = default) =>
        new(_keys.GetValueOrDefault(keyId));
}
