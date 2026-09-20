using SharedKernel.Cryptography.Symmetric;

namespace SharedKernel.Persistence.EfCore.Encryption.BlindIndex;

/// <summary>Computes the HMAC-SHA256 blind index value for a <c>.WithBlindIndex()</c>-annotated encrypted property.</summary>
/// <remarks>
/// Registered by <c>EfCorePersistenceBuilder{TContext}.WithEncryption()</c>. Consumed by
/// <see cref="EncryptionInterceptor"/> (to populate the shadow column on save) and by
/// <see cref="EncryptedPropertyQueryExtensions"/> (to build an equality predicate) — both must use the SAME
/// derivation to ever produce a matching value, so this is the single source of truth for it.
/// </remarks>
public interface IBlindIndexService
{
    /// <summary>
    /// Computes the blind index for <paramref name="normalizedValue"/> under <paramref name="purpose"/>, optionally
    /// bound to one tenant, deriving from whichever key the registered <c>ISynchronousEncryptionKeyProvider</c>
    /// currently reports as current.
    /// </summary>
    /// <param name="purpose">The encrypted property's purpose label, e.g. <c>"customer.email"</c>.</param>
    /// <param name="normalizedValue">
    /// The plaintext, already passed through the property's normalization delegate (or unchanged, if none was
    /// supplied). Callers must apply the identical normalization used at write time — this method does not know
    /// which delegate a given property registered.
    /// </param>
    /// <param name="tenantId">
    /// The tenant id, when the declaring entity is tenanted; otherwise <see langword="null"/>. Must match what was
    /// used when the row was written, or the blind index will not match.
    /// </param>
    /// <returns>The blind index, as lowercase hexadecimal.</returns>
    string Compute(string purpose, string normalizedValue, Guid? tenantId);

    /// <summary>
    /// Computes the blind index exactly like <see cref="Compute(string, string, Guid?)"/>, but derives from
    /// <paramref name="key"/> directly instead of asking the registered <c>ISynchronousEncryptionKeyProvider</c>
    /// what is current.
    /// </summary>
    /// <remarks>
    /// A rotation job resolves its target key asynchronously, from <c>IEncryptionKeyProvider</c> — a DIFFERENT
    /// object than the synchronous provider <see cref="Compute(string, string, Guid?)"/> reads from. When the
    /// synchronous side is a periodically-refreshed bridge over an asynchronous-only provider
    /// (<c>EncryptionKeyRingCache</c>), the two can disagree about "current" for the length of one refresh window —
    /// re-encrypting a row's ciphertext under the fresh asynchronous key while deriving its blind index from the
    /// stale synchronous one leaves that row's blind index permanently unfindable by a normal, consistent lookup.
    /// This overload lets a caller that already resolved the authoritative key (as
    /// <c>EncryptionRotationService{TContext}</c> does, once, at the start of a rotation run) pin the blind index to
    /// that EXACT key, never the synchronous provider's possibly-divergent answer.
    /// </remarks>
    /// <param name="key">The key to derive the blind-index subkey from.</param>
    /// <param name="purpose">The encrypted property's purpose label, e.g. <c>"customer.email"</c>.</param>
    /// <param name="normalizedValue">See <see cref="Compute(string, string, Guid?)"/>.</param>
    /// <param name="tenantId">See <see cref="Compute(string, string, Guid?)"/>.</param>
    /// <returns>The blind index, as lowercase hexadecimal.</returns>
    string Compute(CryptographicKey key, string purpose, string normalizedValue, Guid? tenantId);
}
