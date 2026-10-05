using FluentAssertions;
using MassTransit;
using NSubstitute;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Messaging.MassTransit.Options;
using SharedKernel.Messaging.MassTransit.Serialization;
using SharedKernel.Testing.Cryptography;

// Aliased to avoid the "MassTransit.Configuration"/"MassTransit.Serialization" leaf segments
// colliding with this file's own enclosing namespace tree, SharedKernel.Messaging.MassTransit.*.
using MtSystemTextJsonMessageSerializerFactory = MassTransit.Configuration.SystemTextJsonMessageSerializerFactory;
using MtDictionarySendHeaders = MassTransit.Serialization.DictionarySendHeaders;

namespace SharedKernel.Messaging.MassTransit.Tests.SerializationTests;

/// <summary>
/// PA-11/PA-12/PA-13 (P-499): unit-level proof of the AAD derivation/reconciliation rule inside
/// <see cref="PayloadTransformMessageSerializer"/>/<see cref="PayloadTransformMessageDeserializer"/>,
/// exercised directly against a real <see cref="SynchronousAesGcmEncryptionService"/> and a substitute
/// <see cref="SendContext{T}"/> carrying a real <c>DictionarySendHeaders</c> (which implements both
/// <c>SendHeaders</c> and <c>Headers</c>, so the exact same instance is usable to simulate the
/// header traveling from publish to consume). The full transport-header-really-crosses-the-wire
/// proof is <c>PayloadTransformAadHarnessTests</c> (PA-14), which goes through a real
/// <see cref="MessagingBusBuilder"/>/<c>TestHarness</c> pipeline instead.
/// </summary>
public sealed class PayloadTransformAadTests
{
    [Fact]
    public void RoundTrip_EncryptionEnabled_HeaderCarriesTypeDerivedAad_DecryptsSuccessfully()
    {
        var encryptionService = new SynchronousAesGcmEncryptionService(new FakeEncryptionKeyProvider());
        var options = new PayloadTransformOptions { EnableEncryption = true };

        var serializer = new PayloadTransformMessageSerializer(
            new MtSystemTextJsonMessageSerializerFactory().CreateSerializer(),
            options, compressor: null, encryptionService);
        var deserializer = new PayloadTransformMessageDeserializer(
            new MtSystemTextJsonMessageSerializerFactory().CreateDeserializer(),
            options, compressor: null, encryptionService);

        var headers = new MtDictionarySendHeaders();
        var sendContext = Substitute.For<SendContext<PayloadTransformAadTestMessage>>();
        sendContext.Headers.Returns(headers);
        sendContext.Message.Returns(new PayloadTransformAadTestMessage("hello aad", 1));
        sendContext.SupportedMessageTypes.Returns([MessageUrn.ForTypeString<PayloadTransformAadTestMessage>()]);

        MessageBody publishedBody = serializer.GetMessageBody(sendContext);

        // The publisher must have written the AAD source string into the transport header.
        headers.TryGetHeader(PayloadTransformHeaders.MessageTypeAad, out var headerValue)
            .Should().BeTrue("the publisher must set the AAD transport header when encryption is enabled");
        headerValue.Should().Be(typeof(PayloadTransformAadTestMessage).FullName);

        // The exact same headers instance simulates the header traveling with the message to consume.
        SerializerContext serializerContext = deserializer.Deserialize(
            publishedBody, headers, new Uri("loopback://localhost/test"));

        serializerContext.TryGetMessage<PayloadTransformAadTestMessage>(out var message)
            .Should().BeTrue();
        message.Should().BeEquivalentTo(new PayloadTransformAadTestMessage("hello aad", 1));
    }

    [Fact]
    public void Deserialize_HeaderSwappedToADifferentTypesAad_FailsAuthentication_ThrowsMismatchException()
    {
        var encryptionService = new SynchronousAesGcmEncryptionService(new FakeEncryptionKeyProvider());
        var options = new PayloadTransformOptions { EnableEncryption = true };

        var serializer = new PayloadTransformMessageSerializer(
            new MtSystemTextJsonMessageSerializerFactory().CreateSerializer(),
            options, compressor: null, encryptionService);
        var deserializer = new PayloadTransformMessageDeserializer(
            new MtSystemTextJsonMessageSerializerFactory().CreateDeserializer(),
            options, compressor: null, encryptionService);

        var headers = new MtDictionarySendHeaders();
        var sendContext = Substitute.For<SendContext<PayloadTransformAadTestMessage>>();
        sendContext.Headers.Returns(headers);
        sendContext.Message.Returns(new PayloadTransformAadTestMessage("tampered", 2));
        sendContext.SupportedMessageTypes.Returns([MessageUrn.ForTypeString<PayloadTransformAadTestMessage>()]);

        MessageBody publishedBody = serializer.GetMessageBody(sendContext);

        // Simulate tampering: swap the AAD header to a different type's name after publish, before
        // the message reaches the consumer — mirrors 01.Core's own T-66 headline AAD-mismatch proof.
        headers.Set(PayloadTransformHeaders.MessageTypeAad, "SomeOther.Namespace.DifferentType");

        var act = () => deserializer.Deserialize(publishedBody, headers, new Uri("loopback://localhost/test"));

        act.Should().Throw<PayloadTransformMismatchException>(
            "a mismatched AAD is indistinguishable from a tampered ciphertext and must fail " +
            "authentication rather than silently decrypt under the wrong context");
    }


    [Fact]
    public void GetMessageBody_EncryptionEnabled_BodyIsEncryptedPayloadStorageFormat()
    {
        var encryptionService = new SynchronousAesGcmEncryptionService(new FakeEncryptionKeyProvider("messaging-v1"));
        var options = new PayloadTransformOptions { EnableEncryption = true };

        var serializer = new PayloadTransformMessageSerializer(
            new MtSystemTextJsonMessageSerializerFactory().CreateSerializer(),
            options, compressor: null, encryptionService);

        var headers = new MtDictionarySendHeaders();
        var sendContext = Substitute.For<SendContext<PayloadTransformAadTestMessage>>();
        sendContext.Headers.Returns(headers);
        sendContext.Message.Returns(new PayloadTransformAadTestMessage("storage format", 4));
        sendContext.SupportedMessageTypes.Returns([MessageUrn.ForTypeString<PayloadTransformAadTestMessage>()]);

        byte[] body = serializer.GetMessageBody(sendContext).GetBytes();

        EncryptedPayload.TryParse(body, out EncryptedPayload? payload).Should().BeTrue(
            "the message body is the EncryptedPayload storage format");
        payload!.KeyId.Should().Be("messaging-v1");
        encryptionService
            .Decrypt(payload, System.Text.Encoding.UTF8.GetBytes(typeof(PayloadTransformAadTestMessage).FullName!))
            .IsSuccess.Should().BeTrue("the ciphertext is bound to the message type name");
    }

    [Fact]
    public void Deserialize_NoAadHeaderPresent_ThrowsMismatchException()
    {
        // A body encrypted with empty associated data and sent without the AAD header must not decrypt:
        // the consumer never guesses the associated data.
        var encryptionService = new SynchronousAesGcmEncryptionService(new FakeEncryptionKeyProvider());
        var options = new PayloadTransformOptions { EnableEncryption = true };

        var innerSerializer = new MtSystemTextJsonMessageSerializerFactory().CreateSerializer();
        var sendContext = Substitute.For<SendContext<PayloadTransformAadTestMessage>>();
        sendContext.Message.Returns(new PayloadTransformAadTestMessage("no header", 3));
        sendContext.SupportedMessageTypes.Returns([MessageUrn.ForTypeString<PayloadTransformAadTestMessage>()]);
        byte[] envelopeBytes = innerSerializer.GetMessageBody(sendContext).GetBytes();

        byte[] wireBytes = encryptionService.Encrypt(envelopeBytes, []).ToBytes();

        var deserializer = new PayloadTransformMessageDeserializer(
            new MtSystemTextJsonMessageSerializerFactory().CreateDeserializer(),
            options, compressor: null, encryptionService);

        var act = () => deserializer.Deserialize(
            new BytesMessageBody(wireBytes), new MtDictionarySendHeaders(), new Uri("loopback://localhost/test"));

        act.Should().Throw<PayloadTransformMismatchException>()
            .WithInnerException<InvalidOperationException>()
            .WithMessage($"*{PayloadTransformHeaders.MessageTypeAad}*");
    }
}

/// <summary>
/// Message type — <c>public</c> (not <c>internal</c>), no 'file' modifier. NSubstitute cannot
/// proxy a MassTransit generic interface (here, <see cref="SendContext{T}"/>) closed over an
/// <c>internal</c> type argument — Castle DynamicProxy throws <see cref="ArgumentException"/>
/// because the strong-named <c>MassTransit.Abstractions</c> assembly cannot be granted
/// <c>InternalsVisibleTo</c> for its dynamically-generated proxy assembly. See the
/// `src/Infrastructure/Messaging/CLAUDE.md` Test Rules entry on this exact NSubstitute/strong-naming interaction
/// (originally documented for P-342, reconfirmed here for <see cref="SendContext{T}"/>).
/// </summary>
public sealed record PayloadTransformAadTestMessage(string Text, int Number);
