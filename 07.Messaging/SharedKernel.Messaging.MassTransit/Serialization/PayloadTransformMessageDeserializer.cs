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
/// When decryption is enabled, the body must be an <see cref="EncryptedPayload"/> in its storage
/// format, and the associated data (AAD) needed to authenticate it is read back from the
/// <see cref="PayloadTransformHeaders.MessageTypeAad"/> transport header the publisher set — see
/// <see cref="PayloadTransformMessageSerializer"/>'s remarks. A missing header is a mismatch, never
/// a fallback to empty AAD. Decryption uses
/// <see cref="ISynchronousSymmetricEncryptionService.Decrypt(EncryptedPayload, ReadOnlySpan{byte})"/>
/// for the same structural reason documented on <see cref="PayloadTransformMessageSerializer"/>.
/// </para>
/// </remarks>
internal sealed class PayloadTransformMessageDeserializer : IMessageDeserializer
{
    private readonly IMessageDeserializer _inner;
    private readonly PayloadTransformOptions _options;
    private readonly IPayloadCompressor? _compressor;
    private readonly ISynchronousSymmetricEncryptionService? _encryptionService;

    internal PayloadTransformMessageDeserializer(
        IMessageDeserializer inner,
        PayloadTransformOptions options,
        IPayloadCompressor? compressor,
        ISynchronousSymmetricEncryptionService? encryptionService)
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
                // Reproduce the publisher's AAD from the transport header it set. No header means
                // the message was not produced by an encrypting publisher of this format.
                string aad = headers.Get<string>(PayloadTransformHeaders.MessageTypeAad, null)
                    ?? throw new InvalidOperationException(
                        $"The message has no '{PayloadTransformHeaders.MessageTypeAad}' header, so the " +
                        "associated data needed to decrypt it is unknown.");

                if (!EncryptedPayload.TryParse(bytes, out EncryptedPayload? encrypted))
                    throw new FormatException("The message body is not an encrypted payload.");

                Result<byte[]> decrypted = _encryptionService!.Decrypt(encrypted, Encoding.UTF8.GetBytes(aad));
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
