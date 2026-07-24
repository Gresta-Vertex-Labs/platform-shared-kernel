using Google.Protobuf;
using Microsoft.Extensions.Logging;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Workflows.Temporal.Errors;
using SharedKernel.Workflows.Temporal.Logging;
using Temporalio.Api.Common.V1;
using Temporalio.Converters;

namespace SharedKernel.Workflows.Temporal.Codec;

/// <summary>
/// Wraps <c>01.Core</c>'s <see cref="ISymmetricEncryptionService"/> (AES-256-GCM) as a Temporal
/// <see cref="IPayloadCodec"/>, enabled by <c>.WithPayloadEncryption()</c>.
/// </summary>
/// <remarks>
/// <para>
/// Temporal persists every workflow input, output, signal payload, and activity argument in the
/// server's event history in full, retained for the namespace's whole retention period and readable
/// by anyone with namespace access — including the Temporal Web UI. On a shared or managed cluster
/// that is a permanent plaintext copy of the domain data the workflow touched. This codec is the only
/// thing standing between that data and permanent plaintext storage.
/// </para>
/// <para>
/// Each encoded payload carries the entire original <see cref="Payload"/> (data and metadata) as
/// ciphertext, so decoding restores the original payload byte-for-byte — including whatever encoding
/// metadata the underlying <see cref="IPayloadConverter"/> had set. The encryption key version
/// travels in the encoded payload's own metadata via <see cref="EncryptedPayload.KeyId"/>, so an old
/// key can remain configured for as long as any history encrypted under it might still replay — which
/// for workflows can be months longer than the equivalent database-column case, because a workflow
/// started under key v1 will still replay under key v1 on its final day.
/// </para>
/// <para>
/// A payload not carrying this codec's encoding marker is passed through unchanged on decode — the
/// standard Temporal payload-codec-chain convention, letting multiple codecs coexist. A payload that
/// <em>does</em> carry the marker but fails to decrypt (tamper, wrong key, unknown key id) throws
/// rather than silently passing the ciphertext through as plaintext.
/// </para>
/// </remarks>
internal sealed class EncryptionPayloadCodec : IPayloadCodec
{
    private const string EncodingMetadataKey = "encoding";
    private const string EncodingMetadataValue = "binary/encrypted-sk";
    private const string KeyIdMetadataKey = "sk-encryption-key-id";
    private const string NonceMetadataKey = "sk-encryption-nonce";
    private const string TagMetadataKey = "sk-encryption-tag";

    private readonly ISymmetricEncryptionService _encryptionService;

    public EncryptionPayloadCodec(ISymmetricEncryptionService encryptionService, ILogger<EncryptionPayloadCodec> logger)
    {
        _encryptionService = encryptionService;
        WorkflowLog.PayloadEncryptionConfigured(logger);
    }

    /// <inheritdoc />
    public Task<IReadOnlyCollection<Payload>> EncodeAsync(IReadOnlyCollection<Payload> payloads)
    {
        IReadOnlyCollection<Payload> encoded = payloads.Select(Encode).ToList();
        return Task.FromResult(encoded);
    }

    /// <inheritdoc />
    public Task<IReadOnlyCollection<Payload>> DecodeAsync(IReadOnlyCollection<Payload> payloads)
    {
        IReadOnlyCollection<Payload> decoded = payloads.Select(Decode).ToList();
        return Task.FromResult(decoded);
    }

    private Payload Encode(Payload original)
    {
        byte[] plaintext = original.ToByteArray();
        EncryptedPayload encrypted = _encryptionService.Encrypt(plaintext);

        var result = new Payload
        {
            Data = ByteString.CopyFrom(encrypted.Ciphertext),
        };
        result.Metadata[EncodingMetadataKey] = ByteString.CopyFromUtf8(EncodingMetadataValue);
        result.Metadata[KeyIdMetadataKey] = ByteString.CopyFromUtf8(encrypted.KeyId);
        result.Metadata[NonceMetadataKey] = ByteString.CopyFrom(encrypted.Nonce);
        result.Metadata[TagMetadataKey] = ByteString.CopyFrom(encrypted.Tag);
        return result;
    }

    private Payload Decode(Payload encoded)
    {
        if (!encoded.Metadata.TryGetValue(EncodingMetadataKey, out ByteString? encodingMarker)
            || encodingMarker.ToStringUtf8() != EncodingMetadataValue)
        {
            // Not encoded by this codec — pass through unchanged (the standard codec-chain convention).
            return encoded;
        }

        if (!encoded.Metadata.TryGetValue(KeyIdMetadataKey, out ByteString? keyIdBytes)
            || !encoded.Metadata.TryGetValue(NonceMetadataKey, out ByteString? nonceBytes)
            || !encoded.Metadata.TryGetValue(TagMetadataKey, out ByteString? tagBytes))
        {
            throw new InvalidOperationException(
                WorkflowErrors.PayloadCodecFailure("encoded payload is missing required encryption metadata").Message);
        }

        var encryptedPayload = new EncryptedPayload(
            keyIdBytes.ToStringUtf8(),
            nonceBytes.ToByteArray(),
            encoded.Data.ToByteArray(),
            tagBytes.ToByteArray());

        var decryptResult = _encryptionService.Decrypt(encryptedPayload);
        if (decryptResult.IsFailure)
        {
            throw new InvalidOperationException(
                WorkflowErrors.PayloadCodecFailure(decryptResult.Error.Message).Message);
        }

        return Payload.Parser.ParseFrom(decryptResult.Value);
    }
}
