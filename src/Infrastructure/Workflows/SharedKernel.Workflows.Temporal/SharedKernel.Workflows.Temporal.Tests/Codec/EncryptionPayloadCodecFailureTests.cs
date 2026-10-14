using FluentAssertions;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Primitives.Errors;
using SharedKernel.Testing.Logging;
using SharedKernel.Workflows.Temporal.Codec;
using SharedKernel.Workflows.Temporal.Errors;
using SharedKernel.Workflows.Temporal.Exceptions;
using Temporalio.Api.Common.V1;
using Temporalio.Converters;

namespace SharedKernel.Workflows.Temporal.Tests.Codec;

/// <summary>
/// Every way the codec can fail is one classified <see cref="WorkflowPayloadCodecException"/> carrying
/// <see cref="WorkflowErrorCodes.PayloadCodecFailure"/>, logged once as event 17006 with the exception, with a message and
/// log that name no key id, ciphertext or associated data.
/// </summary>
public sealed class EncryptionPayloadCodecFailureTests
{
    private const string KeyIdMarker = "key-id-that-must-not-leak";
    private const string Namespace = "test-namespace";

    private sealed class SingleKeyProvider(string keyId, byte[] material) : IEncryptionKeyProvider
    {
        public ValueTask<CryptographicKey> GetCurrentKeyAsync(CancellationToken ct = default) =>
            new(new CryptographicKey(keyId, material));

        public ValueTask<CryptographicKey?> GetKeyAsync(string requestedKeyId, CancellationToken ct = default) =>
            new(requestedKeyId == keyId ? new CryptographicKey(keyId, material) : null);
    }

    private static byte[] Key(byte seed) => Enumerable.Repeat(seed, 32).ToArray();

    private static Payload PlaintextPayload(string text)
    {
        var payload = new Payload();
        payload.Metadata["encoding"] = ByteString.CopyFromUtf8("json/plain");
        payload.Data = ByteString.CopyFromUtf8($"\"{text}\"");
        return payload;
    }

    private static (EncryptionPayloadCodec Codec, InMemoryLogger<EncryptionPayloadCodec> Logger) NewCodec(byte keySeed = 1, string keyId = KeyIdMarker)
    {
        var logger = new InMemoryLogger<EncryptionPayloadCodec>();
        var codec = new EncryptionPayloadCodec(new AesGcmEncryptionService(new SingleKeyProvider(keyId, Key(keySeed))), logger);
        return (codec, logger);
    }

    private static async Task<WorkflowPayloadCodecException> CapturedFailureAsync(Func<Task> act) =>
        (await act.Should().ThrowAsync<WorkflowPayloadCodecException>()).Which;

    private static void AssertClassified(WorkflowPayloadCodecException failure, InMemoryLogger<EncryptionPayloadCodec> logger, string operation, params string[] mustNotLeak)
    {
        failure.Error.Code.Should().Be(WorkflowErrorCodes.PayloadCodecFailure);
        failure.Error.Type.Should().Be(ErrorType.Unexpected);
        failure.Message.Should().Be(failure.Error.Message).And.EndWith(".");
        failure.Message.Should().NotContainAny(KeyIdMarker, "ciphertext", "tag", "nonce", "AES");

        LogRecord record = logger.Records.ShouldHaveLogged(new EventId(17006), LogLevel.Error);
        record.Exception.Should().BeSameAs(failure, because: "event 17006 carries the exception");
        logger.Records.ShouldHaveLoggedWithProperty(new EventId(17006), "Operation", operation);
        logger.Records.Where(r => r.EventId.Id == 17006).ToList().Should().ContainSingle();
        logger.Records.ShouldNotLeak(mustNotLeak.Append(KeyIdMarker).ToArray());
        logger.Records.Where(r => r.EventId.Id == 17006).ToList().ShouldFollowLogConvention();
    }

    [Fact]
    public async Task Decode_TamperedCiphertext_IsClassified_LoggedWithTheException_AndLeaksNothing()
    {
        (EncryptionPayloadCodec codec, InMemoryLogger<EncryptionPayloadCodec> logger) = NewCodec();
        Payload encoded = (await codec.EncodeAsync([PlaintextPayload("tamper-target")])).Single().Clone();
        byte[] corrupted = encoded.Data.ToByteArray();
        corrupted[^1] ^= 0xFF;
        encoded.Data = ByteString.CopyFrom(corrupted);

        WorkflowPayloadCodecException failure = await CapturedFailureAsync(() => codec.DecodeAsync([encoded]));

        AssertClassified(failure, logger, "decode", Convert.ToBase64String(corrupted), "tamper-target");
    }

    [Fact]
    public async Task Decode_UnderTheWrongKey_IsClassified()
    {
        (EncryptionPayloadCodec writer, _) = NewCodec(keySeed: 1);
        (EncryptionPayloadCodec reader, InMemoryLogger<EncryptionPayloadCodec> logger) = NewCodec(keySeed: 2);
        IReadOnlyCollection<Payload> encoded = await writer.EncodeAsync([PlaintextPayload("wrong-key-target")]);

        WorkflowPayloadCodecException failure = await CapturedFailureAsync(() => reader.DecodeAsync(encoded));

        AssertClassified(failure, logger, "decode", "wrong-key-target");
    }

    [Fact]
    public async Task Decode_UnderAnUnknownKeyId_IsClassified()
    {
        (EncryptionPayloadCodec writer, _) = NewCodec(keySeed: 1, keyId: "writer-key");
        (EncryptionPayloadCodec reader, InMemoryLogger<EncryptionPayloadCodec> logger) = NewCodec(keySeed: 1, keyId: "reader-key");
        IReadOnlyCollection<Payload> encoded = await writer.EncodeAsync([PlaintextPayload("unknown-key-target")]);

        WorkflowPayloadCodecException failure = await CapturedFailureAsync(() => reader.DecodeAsync(encoded));

        AssertClassified(failure, logger, "decode", "writer-key", "reader-key");
    }

    [Fact]
    public async Task Decode_UnderADifferentWorkflowId_IsClassified()
    {
        (EncryptionPayloadCodec baseCodec, InMemoryLogger<EncryptionPayloadCodec> logger) = NewCodec();
        IPayloadCodec forA = baseCodec.WithSerializationContext(new ISerializationContext.Workflow(Namespace, "wf-a"));
        IPayloadCodec forB = baseCodec.WithSerializationContext(new ISerializationContext.Workflow(Namespace, "wf-b"));
        IReadOnlyCollection<Payload> encoded = await forA.EncodeAsync([PlaintextPayload("aad-target")]);

        WorkflowPayloadCodecException failure = await CapturedFailureAsync(() => forB.DecodeAsync(encoded));

        AssertClassified(failure, logger, "decode", "wf-a", "wf-b", "aad-target");
    }

    [Fact]
    public async Task Decode_AMalformedPayloadCarryingTheMarker_IsClassified()
    {
        (EncryptionPayloadCodec codec, InMemoryLogger<EncryptionPayloadCodec> logger) = NewCodec();
        var malformed = new Payload { Data = ByteString.CopyFromUtf8("not an encrypted payload") };
        malformed.Metadata["encoding"] = ByteString.CopyFromUtf8("binary/encrypted-sk-v2");

        WorkflowPayloadCodecException failure = await CapturedFailureAsync(() => codec.DecodeAsync([malformed]));

        AssertClassified(failure, logger, "decode", "not an encrypted payload");
        failure.InnerException.Should().BeNull();
    }

    private sealed class FaultingKeyProvider(Exception fault) : IEncryptionKeyProvider
    {
        public ValueTask<CryptographicKey> GetCurrentKeyAsync(CancellationToken ct = default) => throw fault;

        public ValueTask<CryptographicKey?> GetKeyAsync(string keyId, CancellationToken ct = default) => throw fault;
    }

    [Fact]
    public async Task Encode_ACryptographicFault_IsClassified_WithTheFaultAsInner()
    {
        var logger = new InMemoryLogger<EncryptionPayloadCodec>();
        var fault = new System.Security.Cryptography.CryptographicException("key store integrity failure");
        var codec = new EncryptionPayloadCodec(new AesGcmEncryptionService(new FaultingKeyProvider(fault)), logger);

        WorkflowPayloadCodecException failure = await CapturedFailureAsync(() => codec.EncodeAsync([PlaintextPayload("encode-target")]));

        AssertClassified(failure, logger, "encode", "encode-target", "key store integrity failure");
        failure.InnerException.Should().BeSameAs(fault);
    }

    [Fact]
    public async Task Encode_AKeyOfTheWrongLength_IsAConfigurationErrorAndPropagatesUnclassified()
    {
        var logger = new InMemoryLogger<EncryptionPayloadCodec>();
        var codec = new EncryptionPayloadCodec(
            new AesGcmEncryptionService(new SingleKeyProvider(KeyIdMarker, new byte[7])), logger);

        Func<Task> act = () => codec.EncodeAsync([PlaintextPayload("encode-target")]);

        await act.Should().ThrowAsync<InvalidOperationException>();
        logger.Records.Should().NotContain(record => record.EventId.Id == 17006);
    }

    [Fact]
    public async Task Encode_AnUnreachableKeyProvider_PropagatesUnclassified()
    {
        var logger = new InMemoryLogger<EncryptionPayloadCodec>();
        var codec = new EncryptionPayloadCodec(
            new AesGcmEncryptionService(new FaultingKeyProvider(new HttpRequestException("key vault unreachable"))), logger);

        Func<Task> act = () => codec.EncodeAsync([PlaintextPayload("encode-target")]);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task Decode_ThenRoundTrip_StillSucceeds_AfterAFailureOnTheSameInstance()
    {
        (EncryptionPayloadCodec codec, _) = NewCodec();
        var malformed = new Payload { Data = ByteString.CopyFromUtf8("x") };
        malformed.Metadata["encoding"] = ByteString.CopyFromUtf8("binary/encrypted-sk-v2");
        Payload original = PlaintextPayload("still-works");

        await CapturedFailureAsync(() => codec.DecodeAsync([malformed]));
        IReadOnlyCollection<Payload> decoded = await codec.DecodeAsync(await codec.EncodeAsync([original]));

        decoded.Single().Should().Be(original);
    }
}
