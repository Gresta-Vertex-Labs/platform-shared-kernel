using System.Collections.Concurrent;
using System.Security.Cryptography;
using SharedKernel.Cryptography;
using SharedKernel.Cryptography.Symmetric;

namespace SharedKernel.Testing.Cryptography;

/// <summary>
/// In-memory test double for <see cref="IEnvelopeEncryptionProvider"/>.
/// </summary>
/// <remarks>
/// <para>
/// Uses a deterministic, NON-cryptographic reversible transform (byte-XOR, repeating a
/// fixed-per-instance "wrap key" across the data key's length) to wrap/unwrap a fresh data key,
/// mirroring <see cref="FakeSymmetricEncryptionService"/>'s own non-cryptographic-internals
/// precedent — never real KMS envelope encryption.
/// </para>
/// <para>
/// <see cref="UnwrapDataKeyAsync(byte[], string, CancellationToken)"/> fails for a
/// <c>wrappedDataKey</c>/<c>masterKeyId</c> pair this fake instance did not itself produce via
/// <see cref="GenerateDataKeyAsync(CancellationToken)"/> — NEVER a plausible-looking-but-wrong
/// unwrap. This is enforced by an internal registry of every wrapped key this instance has
/// produced (the reversible XOR transform alone cannot distinguish "unknown ciphertext" from
/// "valid ciphertext", so the registry check is what supplies that guarantee).
/// </para>
/// <para>
/// <b>THIS IS NEVER SECURE. TEST-ONLY.</b> Never a substitute for a real KMS/HSM-backed
/// <see cref="IEnvelopeEncryptionProvider"/> in anything security-sensitive — the XOR transform
/// provides no confidentiality or integrity guarantee whatsoever, and no master key material ever
/// leaves this process (there is no KMS boundary to protect). Wiring this into a production DI
/// container by accident would make every "wrapped" data key trivially recoverable.
/// </para>
/// </remarks>
public sealed class FakeEnvelopeEncryptionProvider : IEnvelopeEncryptionProvider
{
    private const string DefaultMasterKeyId = "fake-master-v1";

    private readonly int _dataKeyLength;
    private readonly string _masterKeyId;
    private readonly byte[] _wrapKey;
    private readonly ConcurrentDictionary<string, byte[]> _wrappedKeys = new();

    /// <summary>
    /// Initialises a new <see cref="FakeEnvelopeEncryptionProvider"/>.
    /// </summary>
    /// <param name="dataKeyLength">The length, in bytes, of each generated data key. Defaults to 32 (AES-256).</param>
    /// <param name="masterKeyId">
    /// The master key identifier reported on every <see cref="EnvelopeDataKey"/> this instance
    /// produces, and required to match on unwrap. Defaults to <c>"fake-master-v1"</c>.
    /// </param>
    public FakeEnvelopeEncryptionProvider(int dataKeyLength = 32, string masterKeyId = DefaultMasterKeyId)
    {
        if (dataKeyLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dataKeyLength), dataKeyLength, "The data key length must be a positive number of bytes.");
        }

        ArgumentNullException.ThrowIfNull(masterKeyId);

        _dataKeyLength = dataKeyLength;
        _masterKeyId = masterKeyId;
        _wrapKey = RandomNumberGenerator.GetBytes(32);
    }

    /// <summary>
    /// Gets or sets a value indicating whether <see cref="UnwrapDataKeyAsync(byte[], string, CancellationToken)"/>
    /// should unconditionally simulate a KMS-unreachable/unwrap failure, without needing to supply
    /// an unregistered wrapped key. Defaults to <see langword="false"/>.
    /// </summary>
    public bool SimulateFailure { get; set; }

    /// <inheritdoc />
    public ValueTask<EnvelopeDataKey> GenerateDataKeyAsync(CancellationToken ct = default)
    {
        byte[] plaintextKey = RandomNumberGenerator.GetBytes(_dataKeyLength);
        byte[] wrappedKey = Xor(plaintextKey, _wrapKey);

        _wrappedKeys[Convert.ToBase64String(wrappedKey)] = plaintextKey;

        return new ValueTask<EnvelopeDataKey>(new EnvelopeDataKey(plaintextKey, wrappedKey, _masterKeyId));
    }

    /// <inheritdoc />
    public ValueTask<SharedKernel.Primitives.Results.Result<byte[]>> UnwrapDataKeyAsync(byte[] wrappedDataKey, string masterKeyId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(wrappedDataKey);
        ArgumentNullException.ThrowIfNull(masterKeyId);

        if (SimulateFailure)
        {
            return new ValueTask<SharedKernel.Primitives.Results.Result<byte[]>>(SharedKernel.Primitives.Results.Result<byte[]>.Failure(
                SharedKernel.Primitives.Errors.Error.Unexpected(
                    CryptographyErrorCodes.DecryptionFailed,
                    "Envelope unwrap failed: the wrapped data key may have been tampered with or the wrong master key was used.")));
        }

        if (masterKeyId != _masterKeyId
            || !_wrappedKeys.TryGetValue(Convert.ToBase64String(wrappedDataKey), out byte[]? plaintextKey))
        {
            return new ValueTask<SharedKernel.Primitives.Results.Result<byte[]>>(SharedKernel.Primitives.Results.Result<byte[]>.Failure(
                SharedKernel.Primitives.Errors.Error.Unexpected(
                    CryptographyErrorCodes.UnknownKeyId,
                    $"No wrapped data key registered for master key id '{masterKeyId}'.")));
        }

        return new ValueTask<SharedKernel.Primitives.Results.Result<byte[]>>(SharedKernel.Primitives.Results.Result<byte[]>.Success(plaintextKey));
    }

    private static byte[] Xor(byte[] data, byte[] key)
    {
        var output = new byte[data.Length];
        for (int i = 0; i < data.Length; i++)
        {
            output[i] = (byte)(data[i] ^ key[i % key.Length]);
        }

        return output;
    }
}
