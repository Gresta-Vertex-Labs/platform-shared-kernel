using System.Net.Mime;
using MassTransit;
using SharedKernel.Compression;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Messaging.MassTransit.Options;

namespace SharedKernel.Messaging.MassTransit.Serialization;

/// <summary>
/// Decorates an inner <see cref="IMessageSerializer"/> to compress and/or encrypt the serialized
/// message body before it reaches the transport.
/// </summary>
/// <remarks>
/// Fixed ordering: compress first, then encrypt — never the reverse, and never
/// caller-configurable. When both <see cref="PayloadTransformOptions.EnableCompression"/> and
/// <see cref="PayloadTransformOptions.EnableEncryption"/> are <see langword="false"/>, the inner
/// serializer's <see cref="MessageBody"/> is returned unchanged.
/// </remarks>
internal sealed class PayloadTransformMessageSerializer : IMessageSerializer
{
    private readonly IMessageSerializer _inner;
    private readonly PayloadTransformOptions _options;
    private readonly IPayloadCompressor? _compressor;
    private readonly ISymmetricEncryptionService? _encryptionService;

    internal PayloadTransformMessageSerializer(
        IMessageSerializer inner,
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
    public MessageBody GetMessageBody<T>(SendContext<T> context) where T : class
    {
        MessageBody innerBody = _inner.GetMessageBody(context);

        if (!_options.EnableCompression && !_options.EnableEncryption)
            return innerBody;

        byte[] bytes = innerBody.GetBytes();

        // Fixed compress-then-encrypt ordering (01.Core convention) — never caller-configurable.
        if (_options.EnableCompression)
            bytes = _compressor!.Compress(bytes);

        if (_options.EnableEncryption)
        {
            EncryptedPayload encrypted = _encryptionService!.Encrypt(bytes);
            bytes = EncryptedPayloadWireCodec.Encode(encrypted);
        }

        return new BytesMessageBody(bytes);
    }
}
