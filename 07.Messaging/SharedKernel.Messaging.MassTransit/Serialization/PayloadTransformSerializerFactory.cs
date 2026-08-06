using System.Net.Mime;
using MassTransit;
using SharedKernel.Compression;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Messaging.MassTransit.Options;

namespace SharedKernel.Messaging.MassTransit.Serialization;

/// <summary>
/// Decorates an inner <see cref="ISerializerFactory"/> (MassTransit's default System.Text.Json
/// factory) so every produced serializer/deserializer pair applies the configured payload
/// compression/encryption transform.
/// </summary>
/// <remarks>
/// Registered via <c>IBusFactoryConfigurator.AddSerializer(factory, isSerializer: true)</c> by
/// <c>MessagingBusBuilder.WithPayloadTransform</c>. Reports the same
/// <see cref="ContentType"/> as the inner factory, so this decorator becomes the transport's
/// primary (de)serializer for the existing MassTransit JSON content type — no new content-type
/// negotiation is introduced.
/// </remarks>
internal sealed class PayloadTransformSerializerFactory : ISerializerFactory
{
    private readonly ISerializerFactory _inner;
    private readonly PayloadTransformOptions _options;
    private readonly IPayloadCompressor? _compressor;
    private readonly ISymmetricEncryptionService? _encryptionService;

    internal PayloadTransformSerializerFactory(
        ISerializerFactory inner,
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
    public IMessageSerializer CreateSerializer() =>
        new PayloadTransformMessageSerializer(_inner.CreateSerializer(), _options, _compressor, _encryptionService);

    /// <inheritdoc />
    public IMessageDeserializer CreateDeserializer() =>
        new PayloadTransformMessageDeserializer(_inner.CreateDeserializer(), _options, _compressor, _encryptionService);
}
