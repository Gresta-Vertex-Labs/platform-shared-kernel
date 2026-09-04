using SharedKernel.Primitives.Results;

namespace SharedKernel.Cryptography.Symmetric;

/// <summary>
/// Envelope-encryption seam: asks a KMS/HSM-held master key to generate and wrap a fresh
/// symmetric data key, or to unwrap a previously-wrapped one — without the master key material
/// ever leaving the KMS/HSM boundary.
/// </summary>
/// <remarks>
/// <para>
/// Additive to, and architecturally distinct from, <see cref="IEncryptionKeyProvider"/>'s
/// direct-retrieval shape — neither interface is collapsed into the other. A single provider
/// implementation (e.g. an Azure Key Vault-backed one) may implement both: direct retrieval can
/// be built internally on top of envelope wrapping when the underlying KMS does not export raw
/// key material, without changing either contract's shape.
/// </para>
/// <para>
/// Implemented by the consuming service. <c>SharedKernel.Cryptography</c> ships no default
/// implementation and holds no key material of its own. On an unreachable or unauthorized KMS,
/// an implementation MUST propagate a thrown exception — fail-closed is structural to this
/// contract, mirroring <see cref="IEncryptionKeyProvider"/>.
/// </para>
/// </remarks>
public interface IEnvelopeEncryptionProvider
{
    /// <summary>
    /// Asks the KMS to generate a fresh symmetric data key under the provider's own master key,
    /// returning both the plaintext data key for immediate local use and its wrapped form, which
    /// is the only form safe to persist.
    /// </summary>
    /// <param name="ct">A token to observe while generating the data key.</param>
    ValueTask<EnvelopeDataKey> GenerateDataKeyAsync(CancellationToken ct = default);

    /// <summary>
    /// Asks the KMS to unwrap a previously-wrapped data key using the master key identified by
    /// <paramref name="masterKeyId"/>, so master key material never leaves the KMS boundary.
    /// </summary>
    /// <param name="wrappedDataKey">The wrapped data key, as previously returned via <see cref="EnvelopeDataKey.WrappedKey"/>.</param>
    /// <param name="masterKeyId">The identifier of the master key that originally wrapped <paramref name="wrappedDataKey"/>.</param>
    /// <param name="ct">A token to observe while unwrapping the data key.</param>
    /// <returns>
    /// A successful <see cref="Result{T}"/> containing the unwrapped plaintext data key bytes, or
    /// a failed result if the wrapped key is malformed, tampered with, or was not wrapped by
    /// <paramref name="masterKeyId"/>.
    /// </returns>
    ValueTask<Result<byte[]>> UnwrapDataKeyAsync(byte[] wrappedDataKey, string masterKeyId, CancellationToken ct = default);
}
