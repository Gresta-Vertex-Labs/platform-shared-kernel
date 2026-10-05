using System.Net.Mime;
using System.Text;
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
/// <para>
/// Fixed ordering: compress first, then encrypt — never the reverse, and never
/// caller-configurable. When both <see cref="PayloadTransformOptions.EnableCompression"/> and
/// <see cref="PayloadTransformOptions.EnableEncryption"/> are <see langword="false"/>, the inner
/// serializer's <see cref="MessageBody"/> is returned unchanged.
/// </para>
/// <para>
/// When <see cref="PayloadTransformOptions.EnableEncryption"/> is set, the serialized bytes are
/// encrypted under associated data (AAD) derived from the message's own CLR type name
/// (<c>typeof(T).FullName ?? typeof(T).Name</c>), and that same string is written into the
/// <see cref="PayloadTransformHeaders.MessageTypeAad"/> transport header so the consume-side
/// <see cref="PayloadTransformMessageDeserializer"/> — which has no generic <c>T</c> of its own —
/// can reproduce byte-identical AAD before attempting decryption. The message body is the
/// <see cref="EncryptedPayload"/> storage format (<see cref="EncryptedPayload.ToBytes"/>).
/// </para>
/// <para>
/// Encryption goes through <see cref="ISynchronousSymmetricEncryptionService"/>: MassTransit's
/// <see cref="IMessageSerializer.GetMessageBody{T}(SendContext{T})"/> is a hard-synchronous
/// interface member with no async overload anywhere in MassTransit, so this pipeline stage
/// can only use keys an <see cref="ISynchronousEncryptionKeyProvider"/> holds in memory.
/// </para>
/// </remarks>
internal sealed class PayloadTransformMessageSerializer : IMessageSerializer
{
    private readonly IMessageSerializer _inner;
    private readonly PayloadTransformOptions _options;
    private readonly IPayloadCompressor? _compressor;
    private readonly ISynchronousSymmetricEncryptionService? _encryptionService;

    internal PayloadTransformMessageSerializer(
        IMessageSerializer inner,
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
            // AAD source is the message's own CLR type name, written into a transport header so the
            // consume side (no generic T of its own) can reproduce it byte-for-byte before decrypting.
            string aad = typeof(T).FullName ?? typeof(T).Name;
            context.Headers.Set(PayloadTransformHeaders.MessageTypeAad, aad);

            EncryptedPayload encrypted = _encryptionService!.Encrypt(bytes, Encoding.UTF8.GetBytes(aad));
            bytes = encrypted.ToBytes();
        }

        return new BytesMessageBody(bytes);
    }
}
