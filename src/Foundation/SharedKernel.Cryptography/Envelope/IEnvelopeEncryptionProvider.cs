using SharedKernel.Primitives.Results;

namespace SharedKernel.Cryptography.Envelope;

/// <summary>
/// Generates data keys wrapped by a master key held in a key management service, and unwraps them, without the
/// master key ever leaving that service.
/// </summary>
/// <remarks>
/// <para>
/// <b>Untrusted input.</b> <see cref="UnwrapDataKeyAsync"/> receives a master key id and wrapped key read from a
/// stored payload. An implementation must only unwrap with master keys it is configured to use, and must reject any
/// other id without calling the key service.
/// </para>
/// <para>
/// <b>Authenticity.</b> Wrapping with an RSA master key gives confidentiality, not proof of origin: anyone holding the
/// public key can wrap a data key of their choosing, and so can create payloads that decrypt. Where stored payloads
/// can be written by someone who might hold the public key, use a symmetric master key (for example AES key wrap in
/// a managed HSM) or sign the payload.
/// </para>
/// <para>Fail closed: throw when the key service is unreachable or denies access.</para>
/// </remarks>
public interface IEnvelopeEncryptionProvider
{
    /// <summary>Generates a 32-byte data key and wraps it with the current master key.</summary>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>The data key. Dispose it after use.</returns>
    ValueTask<EnvelopeDataKey> GenerateDataKeyAsync(CancellationToken cancellationToken = default);

    /// <summary>Unwraps a data key with the master key <paramref name="masterKeyId"/>.</summary>
    /// <param name="wrappedKey">The wrapped data key.</param>
    /// <param name="masterKeyId">The id of the master key that wrapped it.</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>
    /// The plaintext data key, or a failure when the master key id is not one this provider uses or the wrapped key
    /// is rejected. The caller zeroes the returned bytes after use.
    /// </returns>
    ValueTask<Result<byte[]>> UnwrapDataKeyAsync(
        ReadOnlyMemory<byte> wrappedKey,
        string masterKeyId,
        CancellationToken cancellationToken = default);
}
