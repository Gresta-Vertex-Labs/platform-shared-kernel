using System.Net.Mime;
using System.Text;
using MassTransit;
using SharedKernel.Compression;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Messaging.MassTransit.Options;
using SharedKernel.Primitives.Results;

// Aliased to avoid the "MassTransit.Serialization" leaf segment colliding with this file's own
// enclosing namespace, SharedKernel.Messaging.MassTransit.Serialization.
using MtBodyConsumeContext = MassTransit.Serialization.BodyConsumeContext;

namespace SharedKernel.Messaging.MassTransit.Serialization;

/// <summary>
/// Decorates an inner <see cref="IMessageDeserializer"/> to reverse the compress/encrypt payload
/// transform applied by <see cref="PayloadTransformMessageSerializer"/> before delegating to it.
/// </summary>
/// <remarks>
/// <para>
/// Fixed ordering: decrypt first, then decompress — the exact reverse of the publish-side
/// compress-then-encrypt order, never caller-configurable. Any failure while reversing the
/// transform, or while the inner deserializer parses the resulting bytes, is wrapped in a
/// <see cref="PayloadTransformMismatchException"/> rather than allowed to surface as a confusing
/// raw JSON/decryption/decompression exception — see that type's remarks for why.
/// </para>
/// <para>
/// <b>(P-499/WO-081)</b> When decryption is enabled, the associated data (AAD) needed to
/// authenticate the ciphertext is read back from the <see cref="PayloadTransformHeaders.MessageTypeAad"/>
/// transport header the publisher set — see <see cref="PayloadTransformMessageSerializer"/>'s
/// remarks. A missing header (a message from a pre-P-499 producer) falls back to
/// <see cref="Array.Empty{T}"/>, byte-identical to AES-GCM's own implicit "no AAD" default, so
/// rolling deploys stay safe old-producer→new-consumer. This still calls the <b>synchronous</b>
/// <see cref="ISymmetricEncryptionService.Decrypt(EncryptedPayload, byte[])"/> member for the same
/// structural reason documented on <see cref="PayloadTransformMessageSerializer"/>.
/// </para>
/// </remarks>
internal sealed class PayloadTransformMessageDeserializer : IMessageDeserializer
{
    private readonly IMessageDeserializer _inner;
    private readonly PayloadTransformOptions _options;
    private readonly IPayloadCompressor? _compressor;
    private readonly ISymmetricEncryptionService? _encryptionService;

    internal PayloadTransformMessageDeserializer(
        IMessageDeserializer inner,
        PayloadTransformOptions options,
        IPayloadCompressor? compressor,
        ISymmetricEncryptionService? encryptionService)
    {
        _inner = inner;
        _options = options;
        _compressor = compressor;
        _encryptionService = encryptionService;
    }

    /// <inheritdoc />
    public ContentType ContentType => _inner.ContentType;

    /// <inheritdoc />
    public void Probe(ProbeContext context) => _inner.Probe(context);

    /// <inheritdoc />
    public ConsumeContext Deserialize(ReceiveContext receiveContext)
    {
        SerializerContext serializerContext = Deserialize(
            receiveContext.Body, receiveContext.TransportHeaders, receiveContext.InputAddress);

        return new MtBodyConsumeContext(receiveContext, serializerContext);
    }

    /// <inheritdoc />
    public SerializerContext Deserialize(MessageBody body, Headers headers, Uri? destinationAddress = null)
    {
        MessageBody transformedBody = body;

        if (_options.EnableCompression || _options.EnableEncryption)
            transformedBody = new BytesMessageBody(ReverseTransform(body.GetBytes(), headers));

        try
        {
            return _inner.Deserialize(transformedBody, headers, destinationAddress);
        }
        catch (Exception ex) when (ex is not PayloadTransformMismatchException)
        {
            throw new PayloadTransformMismatchException(
                "Failed to deserialize an incoming message after reversing its payload transform. " +
                "This usually indicates a mismatch between the publisher's and this consumer's " +
                "PayloadTransformOptions (EnableCompression/EnableEncryption values must match).",
                ex);
        }
    }

    /// <inheritdoc />
    public MessageBody GetMessageBody(string text) => _inner.GetMessageBody(text);

    private byte[] ReverseTransform(byte[] bytes, Headers headers)
    {
        try
        {
            // Fixed decrypt-then-decompress ordering — the exact reverse of publish-side
            // compress-then-encrypt.
            if (_options.EnableEncryption)
            {
                // PA-03/PA-09 (P-499): reproduce the publisher's AAD from the transport header it
                // set. Header absent (a message from a pre-PA-* producer) falls back to
                // Array.Empty<byte>() — byte-identical to AES-GCM's own implicit "no AAD"
                // semantics every producer used before this phase, so rolling deploys stay safe in
                // the old-producer/new-consumer direction. The reverse direction (new producer /
                // old consumer) is a genuine, unavoidable AEAD authentication failure — see
                // 07.Messaging/CLAUDE.md's payload-transform section for the operational
                // consequence (consumers must upgrade before producers).
                string? aad = headers.Get<string>(PayloadTransformHeaders.MessageTypeAad, null);
                byte[] aadBytes = aad is not null ? Encoding.UTF8.GetBytes(aad) : [];

                EncryptedPayload encrypted = EncryptedPayloadWireCodec.Decode(bytes);
                Result<byte[]> decrypted = _encryptionService!.Decrypt(encrypted, aadBytes);
                if (decrypted.IsFailure)
                    throw new InvalidOperationException(decrypted.Error.Message);

                bytes = decrypted.Value;
            }

            if (_options.EnableCompression)
            {
                Result<byte[]> decompressed = _compressor!.Decompress(bytes);
                if (decompressed.IsFailure)
                    throw new InvalidOperationException(decompressed.Error.Message);

                bytes = decompressed.Value;
            }

            return bytes;
        }
        catch (Exception ex)
        {
            throw new PayloadTransformMismatchException(
                "Failed to reverse the payload transform (decrypt/decompress) for an incoming " +
                "message. This usually indicates a mismatch between the publisher's and this " +
                "consumer's PayloadTransformOptions (EnableCompression/EnableEncryption values " +
                "must match).",
                ex);
        }
    }
}
