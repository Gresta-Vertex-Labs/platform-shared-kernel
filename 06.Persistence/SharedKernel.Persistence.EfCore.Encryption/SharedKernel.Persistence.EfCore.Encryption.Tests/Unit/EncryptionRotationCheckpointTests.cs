using FluentAssertions;
using SharedKernel.Persistence.EfCore.Encryption.Rotation;

namespace SharedKernel.Persistence.EfCore.Encryption.Tests.Unit;

public sealed class EncryptionRotationCheckpointTests
{
    [Fact]
    public void EncodeThenDecode_RoundTrips()
    {
        var checkpoint = new EncryptionRotationCheckpoint(3, Convert.ToBase64String(Guid.NewGuid().ToByteArray()));

        var token = checkpoint.Encode();
        var decoded = EncryptionRotationCheckpoint.Decode(token);

        decoded.Should().Be(checkpoint);
    }

    [Theory]
    [InlineData("not-base64!!!")]
    [InlineData("")]
    public void Decode_InvalidToken_ThrowsArgumentException(string token)
    {
        var act = () => EncryptionRotationCheckpoint.Decode(token);
        act.Should().Throw<ArgumentException>();
    }
}
