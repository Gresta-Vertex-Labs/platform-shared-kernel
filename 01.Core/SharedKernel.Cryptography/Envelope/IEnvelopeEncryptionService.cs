using SharedKernel.Primitives.Results;

namespace SharedKernel.Cryptography.Envelope;

/// <summary>
/// Encrypts each value under its own data key, wrapped by a master key in a key management service, and stores the
/// wrapped key inside the payload.
/// </summary>
/// <remarks>
/// <para>
/// Every <see cref="EncryptAsync"/> and <see cref="DecryptAsync"/> calls the key service once, to generate or unwrap
/// the data key. That suits large or infrequent values such as files and exports, where a per-value key limits what
/// one leaked key exposes. For many small values, use <c>ISymmetricEncryptionService</c> with a cached key provider.
/// </para>
/// <para>
/// Associated data and failures follow <c>ISymmetricEncryptionService</c>. An unwrap the provider rejects returns
/// <see cref="CryptographyErrorCodes.DataKeyUnwrapFailed"/>. See <see cref="IEnvelopeEncryptionProvider"/> for the
/// authenticity limits of RSA-wrapped keys.
/// </para>
/// </remarks>
public interface IEnvelopeEncryptionService
{
    /// <summary>Encrypts <paramref name="plaintext"/> under a freshly generated data key.</summary>
    /// <param name="plaintext">The bytes to encrypt.</param>
    /// <param name="associatedData">Bytes to authenticate but not store.</param>
    /// <param name="cancellationToken">A token to cancel the key service call.</param>
    /// <returns>The payload.</returns>
    ValueTask<EnvelopePayload> EncryptAsync(
        ReadOnlyMemory<byte> plaintext,
        ReadOnlyMemory<byte> associatedData,
        CancellationToken cancellationToken = default);

    /// <summary>Unwraps the payload's data key and decrypts it.</summary>
    /// <param name="payload">The payload.</param>
    /// <param name="associatedData">The associated data used to encrypt it.</param>
    /// <param name="cancellationToken">A token to cancel the key service call.</param>
    /// <returns>The plaintext, or a failure.</returns>
    ValueTask<Result<byte[]>> DecryptAsync(
        EnvelopePayload payload,
        ReadOnlyMemory<byte> associatedData,
        CancellationToken cancellationToken = default);
}
