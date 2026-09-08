using System.Security.Cryptography;
using SharedKernel.Cryptography.Signing;

namespace SharedKernel.Testing.Cryptography;

/// <summary>
/// In-memory test double for <see cref="IAsymmetricKeyProvider"/>, backed by lazily generated
/// RSA/ECDSA key pairs per key id.
/// </summary>
/// <remarks>
/// <para>
/// Promoted from <c>SharedKernel.Cryptography.Tests</c>' internal <c>InMemoryAsymmetricKeyProvider</c>
/// test double into this package's public, shared surface (zero behavioral drift at promotion time;
/// hardened here for thread safety since fakes in this package may be shared across parallel xUnit
/// collections).
/// </para>
/// <para>
/// <b>BREAKING CHANGE (P-502/WO-081):</b> migrated onto <c>IAsymmetricKeyProvider</c>'s async-only
/// contract (<c>SK.01.P493</c>) — the former synchronous <c>GetRsaKey(string)</c>/<c>GetEcdsaKey(string)</c>
/// members are replaced outright by <see cref="GetRsaKeyAsync(string, CancellationToken)"/>/
/// <see cref="GetEcdsaKeyAsync(string, CancellationToken)"/>, mirroring the real interface's own
/// breaking shape.
/// </para>
/// <para>
/// <b>DESIGN CORRECTION, not a straight port of the pre-migration fake (D-241):</b> the prior
/// version returned a FRESH handle cloned via <c>ExportParameters(true)</c>/<c>RSA.Create(...)</c>
/// on every call, so a caller's <c>using</c>/<see cref="Dispose"/> never invalidated the cached
/// original. Under <c>SK.01.P493</c>'s new, EXPLICIT "the returned instance is NOT caller-owned"
/// contract, that same defensive cloning would silently MASK the exact
/// <see cref="ObjectDisposedException"/> regression <c>SK.01.P493</c> exists to catch (production
/// signature services no longer dispose the provider-returned key). This fake now lazily generates
/// and CACHES ONE <see cref="RSA"/>/<see cref="ECDsa"/> instance per <c>keyId</c>: every subsequent
/// call for the SAME <c>keyId</c> returns the SAME instance (<see cref="ReferenceEquals"/>), never a
/// clone — so a caller-side dispose-then-reuse regression surfaces immediately as an
/// <see cref="ObjectDisposedException"/> on the very next signing/verification call against that
/// same key, inside the consuming test itself. This fake's own <see cref="Dispose"/> still disposes
/// every cached instance, but only at the FAKE's own end-of-life, never per-call.
/// </para>
/// <para>
/// Marked <see cref="ISynchronousAsymmetricKeyProvider"/> — an HONEST claim, not an inferred one:
/// both members below perform no real I/O and always complete via an already-completed
/// <see cref="ValueTask{TResult}"/>, mirroring <see cref="FakeEncryptionKeyProvider"/>'s equivalent
/// claim for the symmetric-encryption side.
/// </para>
/// <para>
/// <b>TEST-ONLY — NEVER PRODUCTION-SAFE.</b> RSA/ECDSA key pairs are generated in-process and held
/// only in memory with zero persistence or HSM/Key Vault-backed hardening — wiring this into a
/// production DI container would silently discard every key pair on process restart.
/// </para>
/// </remarks>
public sealed class FakeAsymmetricKeyProvider : ISynchronousAsymmetricKeyProvider, IDisposable
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, RSA> _rsaKeys = [];
    private readonly Dictionary<string, ECDsa> _ecdsaKeys = [];

    /// <inheritdoc />
    /// <remarks>
    /// Completes synchronously via an already-completed <see cref="ValueTask{TResult}"/> — this
    /// fake performs no real I/O. Returns the SAME cached instance across repeated calls for the
    /// same <paramref name="keyId"/> — see class remarks.
    /// </remarks>
    public ValueTask<RSA> GetRsaKeyAsync(string keyId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(keyId);

        lock (_gate)
        {
            if (!_rsaKeys.TryGetValue(keyId, out RSA? existing))
            {
                existing = RSA.Create(2048);
                _rsaKeys[keyId] = existing;
            }

            return new ValueTask<RSA>(existing);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Completes synchronously via an already-completed <see cref="ValueTask{TResult}"/> — this
    /// fake performs no real I/O. Returns the SAME cached instance across repeated calls for the
    /// same <paramref name="keyId"/> — see class remarks.
    /// </remarks>
    public ValueTask<ECDsa> GetEcdsaKeyAsync(string keyId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(keyId);

        lock (_gate)
        {
            if (!_ecdsaKeys.TryGetValue(keyId, out ECDsa? existing))
            {
                existing = ECDsa.Create(ECCurve.NamedCurves.nistP256);
                _ecdsaKeys[keyId] = existing;
            }

            return new ValueTask<ECDsa>(existing);
        }
    }

    /// <summary>Disposes every cached RSA/ECDSA key pair, exactly once each, at end of life.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            foreach (RSA rsa in _rsaKeys.Values)
            {
                rsa.Dispose();
            }

            foreach (ECDsa ecdsa in _ecdsaKeys.Values)
            {
                ecdsa.Dispose();
            }
        }
    }
}
