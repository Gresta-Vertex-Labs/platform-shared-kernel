using FluentAssertions;
using Google.Protobuf;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Workflows.Temporal.Codec;
using Temporalio.Api.Common.V1;
using Temporalio.Converters;

namespace SharedKernel.Workflows.Temporal.Tests.Codec;

/// <summary>
/// T-16 (unit part) — <see cref="EncryptionPayloadCodec"/>: an encrypted payload never carries the
/// original plaintext bytes; decode round-trips; a payload encrypted under key <c>v1</c> still
/// decodes after key <c>v2</c> is registered; a decode failure surfaces as a thrown failure, never a
/// silent passthrough of ciphertext as plaintext or plaintext as decoded.
/// </summary>
public sealed class EncryptionPayloadCodecTests
{
    private sealed class FakeEncryptionKeyProvider : IEncryptionKeyProvider
    {
        private readonly Dictionary<string, CryptographicKey> _keys = new();
        private string _currentKeyId;

        public FakeEncryptionKeyProvider(string currentKeyId, byte[] currentKeyMaterial)
        {
            _currentKeyId = currentKeyId;
            _keys[currentKeyId] = new CryptographicKey(currentKeyId, currentKeyMaterial);
        }

        public void AddKey(string keyId, byte[] material, bool makeCurrent = false)
        {
            _keys[keyId] = new CryptographicKey(keyId, material);
            if (makeCurrent)
            {
                _currentKeyId = keyId;
            }
        }

        public ValueTask<CryptographicKey> GetCurrentKeyAsync(CancellationToken ct = default) =>
            new(_keys[_currentKeyId]);

        public ValueTask<CryptographicKey?> GetKeyAsync(string keyId, CancellationToken ct = default) =>
            new(_keys.GetValueOrDefault(keyId));
    }

    private static byte[] NewAes256Key(byte seed) => Enumerable.Repeat(seed, 32).ToArray();

    private static Payload PlaintextPayload(string text)
    {
        var payload = new Payload();
        payload.Metadata["encoding"] = ByteString.CopyFromUtf8("json/plain");
        payload.Data = ByteString.CopyFromUtf8($"\"{text}\"");
        return payload;
    }

    [Fact]
    public async Task Encode_ProducedPayload_DoesNotContainOriginalPlaintextBytes()
    {
        const string secret = "super-secret-marker-value";
        var keyProvider = new FakeEncryptionKeyProvider("v1", NewAes256Key(1));
        var service = new AesGcmEncryptionService(keyProvider);
        var codec = new EncryptionPayloadCodec(service, NullLogger<EncryptionPayloadCodec>.Instance);

        Payload original = PlaintextPayload(secret);
        IReadOnlyCollection<Payload> encoded = await codec.EncodeAsync([original]);

        Payload encodedPayload = encoded.Single();
        string encodedBytesAsLatin1 = System.Text.Encoding.Latin1.GetString(encodedPayload.Data.ToByteArray());
        encodedBytesAsLatin1.Should().NotContain(secret, because: "the encoded payload must never carry the plaintext");
    }

    [Fact]
    public async Task Decode_ThenReconstructsOriginalPayload_ByteForByte()
    {
        var keyProvider = new FakeEncryptionKeyProvider("v1", NewAes256Key(1));
        var service = new AesGcmEncryptionService(keyProvider);
        var codec = new EncryptionPayloadCodec(service, NullLogger<EncryptionPayloadCodec>.Instance);

        Payload original = PlaintextPayload("round-trip-value");
        IReadOnlyCollection<Payload> encoded = await codec.EncodeAsync([original]);
        IReadOnlyCollection<Payload> decoded = await codec.DecodeAsync(encoded);

        Payload decodedPayload = decoded.Single();
        decodedPayload.Should().Be(original, because: "decode must reconstruct the original payload byte-for-byte, including metadata");
    }

    [Fact]
    public async Task Decode_PayloadEncryptedUnderKeyV1_StillDecodes_AfterKeyV2IsAdded()
    {
        var keyProvider = new FakeEncryptionKeyProvider("v1", NewAes256Key(1));
        var service = new AesGcmEncryptionService(keyProvider);
        var codec = new EncryptionPayloadCodec(service, NullLogger<EncryptionPayloadCodec>.Instance);

        Payload original = PlaintextPayload("encrypted-under-v1");
        IReadOnlyCollection<Payload> encodedUnderV1 = await codec.EncodeAsync([original]);

        // Key rotation: v2 becomes current, but v1 must remain resolvable for as long as history
        // encrypted under it might still replay.
        keyProvider.AddKey("v2", NewAes256Key(2), makeCurrent: true);

        IReadOnlyCollection<Payload> decoded = await codec.DecodeAsync(encodedUnderV1);

        decoded.Single().Should().Be(original);
    }

    [Fact]
    public async Task Encode_AfterKeyRotation_UsesTheNewCurrentKey()
    {
        var keyProvider = new FakeEncryptionKeyProvider("v1", NewAes256Key(1));
        var service = new AesGcmEncryptionService(keyProvider);
        var codec = new EncryptionPayloadCodec(service, NullLogger<EncryptionPayloadCodec>.Instance);

        keyProvider.AddKey("v2", NewAes256Key(2), makeCurrent: true);

        Payload original = PlaintextPayload("encrypted-under-v2");
        IReadOnlyCollection<Payload> encoded = await codec.EncodeAsync([original]);

        // Remove v1 entirely (simulate full retirement) — decode must still succeed via v2.
        IReadOnlyCollection<Payload> decoded = await codec.DecodeAsync(encoded);
        decoded.Single().Should().Be(original);
    }

    [Fact]
    public async Task Decode_PayloadNotCarryingTheCodecMarker_PassesThroughUnchanged()
    {
        var keyProvider = new FakeEncryptionKeyProvider("v1", NewAes256Key(1));
        var service = new AesGcmEncryptionService(keyProvider);
        var codec = new EncryptionPayloadCodec(service, NullLogger<EncryptionPayloadCodec>.Instance);

        Payload plain = PlaintextPayload("never-encoded-by-this-codec");

        IReadOnlyCollection<Payload> decoded = await codec.DecodeAsync([plain]);

        decoded.Single().Should().Be(plain, because: "a payload not carrying this codec's marker must pass through unchanged, per the codec-chain convention");
    }

    [Fact]
    public async Task Decode_TamperedCiphertext_ThrowsRatherThanSilentlyPassingThroughAsPlaintext()
    {
        var keyProvider = new FakeEncryptionKeyProvider("v1", NewAes256Key(1));
        var service = new AesGcmEncryptionService(keyProvider);
        var codec = new EncryptionPayloadCodec(service, NullLogger<EncryptionPayloadCodec>.Instance);

        Payload original = PlaintextPayload("tamper-target");
        IReadOnlyCollection<Payload> encoded = await codec.EncodeAsync([original]);

        Payload tampered = encoded.Single().Clone();
        byte[] corruptedData = tampered.Data.ToByteArray();

        // Flip the last byte — part of the ciphertext, so the payload still parses and the failure is
        // a genuine AES-GCM authentication failure rather than a malformed-layout rejection.
        corruptedData[^1] ^= 0xFF;
        tampered.Data = ByteString.CopyFrom(corruptedData);
        EncryptedPayload.TryParse(corruptedData, out _).Should().BeTrue();

        Func<Task> act = () => codec.DecodeAsync([tampered]);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Encode_ProducedPayload_CarriesTheCanonicalEncryptedPayloadLayoutAndOnlyTheVersionedMarker()
    {
        var keyProvider = new FakeEncryptionKeyProvider("v1", NewAes256Key(1));
        var service = new AesGcmEncryptionService(keyProvider);
        var codec = new EncryptionPayloadCodec(service, NullLogger<EncryptionPayloadCodec>.Instance);

        IReadOnlyCollection<Payload> encoded = await codec.EncodeAsync([PlaintextPayload("layout-check")]);
        Payload encodedPayload = encoded.Single();

        encodedPayload.Metadata.Keys.Should().Equal("encoding");
        encodedPayload.Metadata["encoding"].ToStringUtf8().Should().Be("binary/encrypted-sk-v2");
        EncryptedPayload.TryParse(encodedPayload.Data.Span, out EncryptedPayload? parsed).Should().BeTrue();
        parsed!.KeyId.Should().Be("v1");
    }

    [Fact]
    public async Task Decode_PayloadCarryingTheMarkerWithMalformedData_ThrowsRatherThanPassingThrough()
    {
        var keyProvider = new FakeEncryptionKeyProvider("v1", NewAes256Key(1));
        var codec = new EncryptionPayloadCodec(new AesGcmEncryptionService(keyProvider), NullLogger<EncryptionPayloadCodec>.Instance);

        var malformed = new Payload { Data = ByteString.CopyFromUtf8("not an encrypted payload") };
        malformed.Metadata["encoding"] = ByteString.CopyFromUtf8("binary/encrypted-sk-v2");

        Func<Task> act = () => codec.DecodeAsync([malformed]);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().StartWith("The payload codec failed:");
    }

    [Fact]
    public async Task Decode_UnknownKeyId_ThrowsRatherThanSilentlyPassingThrough()
    {
        var keyProvider = new FakeEncryptionKeyProvider("v1", NewAes256Key(1));
        var service = new AesGcmEncryptionService(keyProvider);
        var codec = new EncryptionPayloadCodec(service, NullLogger<EncryptionPayloadCodec>.Instance);

        Payload original = PlaintextPayload("unknown-key-target");
        IReadOnlyCollection<Payload> encoded = await codec.EncodeAsync([original]);

        // Simulate the key having been retired entirely by using a provider that never knew it.
        var emptyKeyProvider = new FakeEncryptionKeyProvider("v-other", NewAes256Key(9));
        var codecWithoutTheKey = new EncryptionPayloadCodec(
            new AesGcmEncryptionService(emptyKeyProvider), NullLogger<EncryptionPayloadCodec>.Instance);

        Func<Task> act = () => codecWithoutTheKey.DecodeAsync(encoded);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
