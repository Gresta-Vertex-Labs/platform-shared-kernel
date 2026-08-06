using System.Text;
using FluentAssertions;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Messaging.MassTransit.Serialization;

namespace SharedKernel.Messaging.MassTransit.Tests.SerializationTests;

/// <summary>
/// Round-trip and malformed-input tests for the internal <see cref="EncryptedPayloadWireCodec"/>.
/// </summary>
public sealed class EncryptedPayloadWireCodecTests
{
    [Fact]
    public void Encode_ThenDecode_RoundTripsAllFields()
    {
        var payload = new EncryptedPayload(
            KeyId: "v1",
            Nonce: [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12],
            Ciphertext: Encoding.UTF8.GetBytes("some ciphertext bytes"),
            Tag: [9, 8, 7, 6, 5, 4, 3, 2, 1, 0, 1, 2, 3, 4, 5, 6]);

        var encoded = EncryptedPayloadWireCodec.Encode(payload);
        var decoded = EncryptedPayloadWireCodec.Decode(encoded);

        decoded.Should().BeEquivalentTo(payload);
    }

    [Fact]
    public void Encode_ThenDecode_HandlesEmptyCiphertext()
    {
        var payload = new EncryptedPayload("v1", [1, 2, 3], [], [4, 5, 6]);

        var decoded = EncryptedPayloadWireCodec.Decode(EncryptedPayloadWireCodec.Encode(payload));

        decoded.Should().BeEquivalentTo(payload);
    }

    [Fact]
    public void Encode_ThenDecode_HandlesEmptyKeyId()
    {
        var payload = new EncryptedPayload(string.Empty, [1], [2], [3]);

        var decoded = EncryptedPayloadWireCodec.Decode(EncryptedPayloadWireCodec.Encode(payload));

        decoded.Should().BeEquivalentTo(payload);
    }

    [Fact]
    public void Decode_TruncatedBuffer_ThrowsFormatException()
    {
        byte[] truncated = [0, 0, 0, 5, 1, 2]; // claims a 5-byte segment but only 2 bytes follow

        var act = () => EncryptedPayloadWireCodec.Decode(truncated);

        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Decode_EmptyBuffer_ThrowsFormatException()
    {
        var act = () => EncryptedPayloadWireCodec.Decode([]);

        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Decode_ArbitraryJsonBytes_ThrowsFormatException()
    {
        // Simulates feeding a plain (unencrypted) JSON payload into the codec — the exact
        // mismatched-configuration scenario PT-03/PT-11 guard against.
        byte[] jsonBytes = "{\"hello\":\"world\"}"u8.ToArray();

        var act = () => EncryptedPayloadWireCodec.Decode(jsonBytes);

        act.Should().Throw<FormatException>();
    }
}
