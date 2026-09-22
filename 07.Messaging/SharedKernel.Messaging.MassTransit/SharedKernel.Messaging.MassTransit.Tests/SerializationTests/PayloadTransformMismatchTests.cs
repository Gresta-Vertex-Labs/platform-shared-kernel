using FluentAssertions;
using MassTransit;
using SharedKernel.Compression;
using SharedKernel.Compression.Options;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Messaging.MassTransit.Options;
using SharedKernel.Messaging.MassTransit.Serialization;
using SharedKernel.Testing.Cryptography;

// Aliased to avoid the "MassTransit.Configuration"/"MassTransit.Serialization" leaf segments
// colliding with this file's own enclosing namespace tree, SharedKernel.Messaging.MassTransit.*.
using MtSystemTextJsonMessageSerializerFactory = MassTransit.Configuration.SystemTextJsonMessageSerializerFactory;
using MtEmptyHeaders = MassTransit.Serialization.DictionarySendHeaders;

namespace SharedKernel.Messaging.MassTransit.Tests.SerializationTests;

/// <summary>
/// PT-11: Mismatched-configuration test — a consumer whose <see cref="PayloadTransformOptions"/>
/// does not match the publisher's must fail loudly with <see cref="PayloadTransformMismatchException"/>,
/// never a silently misinterpreted payload.
/// </summary>
/// <remarks>
/// Exercises <see cref="PayloadTransformMessageDeserializer"/> directly (rather than through a full
/// MassTransit bus/harness) so both mismatch directions — publisher-transformed/consumer-not, and
/// publisher-plain/consumer-expects-transformed — can be proven deterministically without needing
/// two interoperating bus instances.
/// </remarks>
public sealed class PayloadTransformMismatchTests
{
    [Fact]
    public void Deserialize_PublisherCompressedAndEncrypted_ConsumerTransformDisabled_ThrowsMismatchException()
    {
        // Arrange: bytes as if a publisher had compressed-then-encrypted them.
        var compressor = new BrotliPayloadCompressor(Microsoft.Extensions.Options.Options.Create(new CompressionOptions()));
        var encryptionService = new SynchronousAesGcmEncryptionService(new FakeEncryptionKeyProvider());

        byte[] plainBytes = "{\"hello\":\"world\"}"u8.ToArray();
        byte[] compressed = compressor.Compress(plainBytes);
        EncryptedPayload encrypted = encryptionService.Encrypt(compressed, []);
        byte[] wireBytes = encrypted.ToBytes();

        var innerDeserializer = new MtSystemTextJsonMessageSerializerFactory().CreateDeserializer();

        // Consumer has the transform wired but disabled — mirrors "consumer never enabled it".
        var deserializer = new PayloadTransformMessageDeserializer(
            innerDeserializer,
            new PayloadTransformOptions(),
            compressor: null,
            encryptionService: null);

        var body = new BytesMessageBody(wireBytes);

        var act = () => deserializer.Deserialize(body, new MtEmptyHeaders(), new Uri("loopback://localhost/test"));

        act.Should().Throw<PayloadTransformMismatchException>()
            .WithInnerException<Exception>();
    }

    [Fact]
    public void Deserialize_PublisherPlainJson_ConsumerExpectsEncryption_ThrowsMismatchException()
    {
        // Arrange: plain, untransformed JSON bytes as if the publisher never enabled the transform.
        byte[] plainBytes = "{\"hello\":\"world\"}"u8.ToArray();

        var innerDeserializer = new MtSystemTextJsonMessageSerializerFactory().CreateDeserializer();
        var encryptionService = new SynchronousAesGcmEncryptionService(new FakeEncryptionKeyProvider());

        // Consumer expects the payload to have been encrypted.
        var deserializer = new PayloadTransformMessageDeserializer(
            innerDeserializer,
            new PayloadTransformOptions { EnableEncryption = true },
            compressor: null,
            encryptionService: encryptionService);

        var body = new BytesMessageBody(plainBytes);

        // The AAD header is present, so the failure comes from the body not being an encrypted payload.
        var headers = new MtEmptyHeaders();
        headers.Set(PayloadTransformHeaders.MessageTypeAad, "Some.Message.Type");

        var act = () => deserializer.Deserialize(body, headers, new Uri("loopback://localhost/test"));

        act.Should().Throw<PayloadTransformMismatchException>()
            .WithInnerException<FormatException>();
    }

    [Fact]
    public void Deserialize_PublisherPlainJson_ConsumerExpectsCompression_ThrowsMismatchException()
    {
        byte[] plainBytes = "{\"hello\":\"world\"}"u8.ToArray();

        var innerDeserializer = new MtSystemTextJsonMessageSerializerFactory().CreateDeserializer();
        var compressor = new BrotliPayloadCompressor(Microsoft.Extensions.Options.Options.Create(new CompressionOptions()));

        var deserializer = new PayloadTransformMessageDeserializer(
            innerDeserializer,
            new PayloadTransformOptions { EnableCompression = true },
            compressor: compressor,
            encryptionService: null);

        var body = new BytesMessageBody(plainBytes);

        var act = () => deserializer.Deserialize(body, new MtEmptyHeaders(), new Uri("loopback://localhost/test"));

        act.Should().Throw<PayloadTransformMismatchException>()
            .WithInnerException<Exception>();
    }
}
